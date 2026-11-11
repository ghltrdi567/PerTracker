namespace PerFunTracker.Core.Options;

/// <summary>
/// Debug flags, bound from the "Debug" section.
/// В репо всегда false — флаг включается при деплое через env
/// (Debug__SeeAllEvents=true на Render / в docker compose), не через appsettings.
///
/// SeeAllEvents: админ видит ВСЕ события (список, детали, завершения),
/// а не только свои/участником которых он является. Пока флаг включён,
/// фронтенд показывает всем пользователям предупреждение на странице событий.
/// </summary>
public class DebugOptions
{
    public const string SectionName = "Debug";

    public bool SeeAllEvents { get; set; }
}
