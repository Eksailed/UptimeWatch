using System.Globalization;
using UptimeWatch.Web.Localization;

namespace UptimeWatch.Integration.Tests.Localization;

public sealed class CultureScopeTests
{
    [Fact]
    public async Task Scope_sets_the_culture_and_restores_the_previous_one()
    {
        // A thread-pool thread of its own: the culture of the test thread must not leak into other tests.
        await Task.Run(() =>
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

            using (CultureScope.Enter(new CultureInfo("ru"), new CultureInfo("en")))
            {
                Assert.Equal("ru", CultureInfo.CurrentCulture.Name);
                Assert.Equal("en", CultureInfo.CurrentUICulture.Name);
                Assert.Equal("31.10.2026 08:24:56", new DateTime(2026, 10, 31, 8, 24, 56).ToString("G", CultureInfo.CurrentCulture));
            }

            Assert.Equal(CultureInfo.InvariantCulture, CultureInfo.CurrentCulture);
            Assert.Equal(CultureInfo.InvariantCulture, CultureInfo.CurrentUICulture);
        });
    }
}
