using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;

namespace UmbracoDocker.Startup;

/// <summary>
/// Wires up the one-time demo site installer that runs after Umbraco has booted.
/// </summary>
public class DemoSiteComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder) =>
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, DemoSiteInstaller>();
}
