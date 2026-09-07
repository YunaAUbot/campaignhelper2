namespace CampaignHelper.Tests;
using Xunit;
public sealed class BundledDataTests
{
    [Fact] public void ApprovedBundledDataValidates()
    {
        var root=Path.Combine(AppContext.BaseDirectory,"Data");
        var guide=CampaignGuide.Parse(File.ReadAllText(Path.Combine(root,"guide.json")),File.ReadAllText(Path.Combine(root,"areas.json")));
        Assert.True(guide.PagesFor(new(true,true)).Count()>100); Assert.True(guide.Areas.Count>50);
    }
}
