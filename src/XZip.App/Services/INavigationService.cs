using Microsoft.UI.Xaml.Controls;

namespace XZip.App.Services;

public interface INavigationService
{
    /// <summary>The Frame that hosts navigated pages. Set once on app startup.</summary>
    Frame? Frame { get; set; }

    /// <summary>Currently shown page type.</summary>
    Type? CurrentPageType { get; }

    /// <summary>Navigate to a page by type.</summary>
    bool NavigateTo(Type pageType, object? parameter = null);

    /// <summary>Navigate to a page by tag (e.g. NavigationView item Tag).</summary>
    bool NavigateToTag(string tag, object? parameter = null);

    /// <summary>Register a tag → page-type mapping.</summary>
    void Register(string tag, Type pageType);

    event EventHandler<Type>? Navigated;
}

public sealed class NavigationService : INavigationService
{
    private readonly Dictionary<string, Type> _routes = new(StringComparer.OrdinalIgnoreCase);

    public Frame? Frame { get; set; }

    public Type? CurrentPageType { get; private set; }

    public event EventHandler<Type>? Navigated;

    public void Register(string tag, Type pageType) => _routes[tag] = pageType;

    public bool NavigateToTag(string tag, object? parameter = null)
    {
        return _routes.TryGetValue(tag, out var pageType) && NavigateTo(pageType, parameter);
    }

    public bool NavigateTo(Type pageType, object? parameter = null)
    {
        if (Frame is null) return false;
        if (Frame.CurrentSourcePageType == pageType) return true;

        var transition = new Microsoft.UI.Xaml.Media.Animation.SlideNavigationTransitionInfo
        {
            Effect = Microsoft.UI.Xaml.Media.Animation.SlideNavigationTransitionEffect.FromRight,
        };
        var ok = Frame.Navigate(pageType, parameter, transition);
        if (ok)
        {
            CurrentPageType = pageType;
            Navigated?.Invoke(this, pageType);
        }
        return ok;
    }
}
