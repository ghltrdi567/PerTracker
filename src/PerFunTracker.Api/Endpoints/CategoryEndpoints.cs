using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerFunTracker.Api.Models;
using PerFunTracker.Core.Entities;
using PerFunTracker.Infrastructure.Data;

namespace PerFunTracker.Api.Endpoints;

/// <summary>
/// Category management endpoints. Only admins can manage categories.
/// All endpoints require the "Admin" policy.
/// </summary>
public static class CategoryEndpoints
{
    public static IEndpointRouteBuilder MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/categories").RequireAuthorization("Admin");

        group.MapGet("/", ListCategories);
        group.MapPost("/", CreateCategory);
        group.MapDelete("/{id:guid}", DeleteCategory);

        return app;
    }

    // --- GET /api/categories — list all categories (admin only) ---

    private static async Task<Ok<PagedResult<CategoryResponse>>> ListCategories(
        ApplicationDbContext db,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);

        var baseQuery = db.Categories.AsQueryable();

        var total = await baseQuery.CountAsync();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);

        var items = await baseQuery
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CategoryResponse(c.Id, c.Name, c.Icon))
            .ToListAsync();

        var result = new PagedResult<CategoryResponse>(items, page, pageSize, total, totalPages);
        return TypedResults.Ok(result);
    }

    // --- POST /api/categories — create a new category (admin only) ---

    private static async Task<Results<Created<CategoryResponse>, BadRequest<ErrorResponse>>> CreateCategory(
        CreateCategoryRequest request,
        ApplicationDbContext db,
        HttpContext ctx)
    {
        // Check for duplicate name (case-insensitive via PostgreSQL ILIKE)
        if (await db.Categories.AnyAsync(c => EF.Functions.ILike(c.Name, request.Name)))
            return TypedResults.BadRequest(new ErrorResponse(["A category with this name already exists."]));

        var category = new Category
        {
            Name = request.Name,
            Icon = request.Icon,
            CreatedAt = DateTime.UtcNow,
        };

        db.Categories.Add(category);
        await db.SaveChangesAsync();

        var response = new CategoryResponse(category.Id, category.Name, category.Icon);
        return TypedResults.Created($"/api/categories/{category.Id}", response);
    }

    // --- DELETE /api/categories/{id} — delete a category (admin only) ---

    private static async Task<Results<NoContent, NotFound>> DeleteCategory(
        Guid id,
        ApplicationDbContext db)
    {
        var category = await db.Categories.FindAsync(id);
        if (category is null)
            return TypedResults.NotFound();

        db.Categories.Remove(category);
        await db.SaveChangesAsync();

        return TypedResults.NoContent();
    }
}
