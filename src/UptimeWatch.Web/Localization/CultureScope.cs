using System.Globalization;

namespace UptimeWatch.Web.Localization;

/// <summary>
/// Runs a piece of work in a given culture. A Blazor circuit starts in the culture of the request, but a render that
/// is started from a background thread (a status update from the checker) runs in that thread's culture instead,
/// so such handlers capture the culture on the circuit and enter it again around the render.
/// </summary>
public sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _previousUiCulture = CultureInfo.CurrentUICulture;

    private CultureScope(CultureInfo culture, CultureInfo uiCulture)
    {
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = uiCulture;
    }

    public static CultureScope Enter(CultureInfo culture, CultureInfo uiCulture) => new(culture, uiCulture);

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _previousCulture;
        CultureInfo.CurrentUICulture = _previousUiCulture;
    }
}
