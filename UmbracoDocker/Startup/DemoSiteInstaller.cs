using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.ContentPublishing;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.OperationStatus;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Core.Sync;

namespace UmbracoDocker.Startup;

/// <summary>
/// Creates a "Home Page" document type, its template and a published Home node the first
/// time the site boots on an empty database. Running it again is a no-op, so a rebuilt
/// container against an existing volume leaves content alone.
/// </summary>
public class DemoSiteInstaller : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private const string ContentTypeAlias = "homePage";
    private const string TemplateAlias = "homePage";

    // Built-in data types, looked up by key so we don't depend on their names.
    private static readonly Guid TextstringDataType = new("0cc0eba1-9960-42c9-bf9b-60e150b429ae");
    private static readonly Guid RichtextDataType = new("ca90c950-0aff-4e72-b976-a30b1ac57dad");

    private readonly IContentTypeService _contentTypeService;
    private readonly IContentService _contentService;
    private readonly IContentPublishingService _contentPublishingService;
    private readonly ITemplateService _templateService;
    private readonly IDataTypeService _dataTypeService;
    private readonly IShortStringHelper _shortStringHelper;
    private readonly IRuntimeState _runtimeState;
    private readonly IServerRoleAccessor _serverRoleAccessor;
    private readonly ILogger<DemoSiteInstaller> _logger;

    public DemoSiteInstaller(
        IContentTypeService contentTypeService,
        IContentService contentService,
        IContentPublishingService contentPublishingService,
        ITemplateService templateService,
        IDataTypeService dataTypeService,
        IShortStringHelper shortStringHelper,
        IRuntimeState runtimeState,
        IServerRoleAccessor serverRoleAccessor,
        ILogger<DemoSiteInstaller> logger)
    {
        _contentTypeService = contentTypeService;
        _contentService = contentService;
        _contentPublishingService = contentPublishingService;
        _templateService = templateService;
        _dataTypeService = dataTypeService;
        _shortStringHelper = shortStringHelper;
        _runtimeState = runtimeState;
        _serverRoleAccessor = serverRoleAccessor;
        _logger = logger;
    }

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Demo site installer: runtime level {Level}, server role {Role}.",
            _runtimeState.Level,
            _serverRoleAccessor.CurrentServerRole);

        if (_runtimeState.Level != RuntimeLevel.Run)
        {
            return;
        }

        // No server-role gate on purpose: at application-started time Umbraco has not run its
        // server election yet and every instance reports itself as Subscriber. This setup is a
        // single container, and the Get() check below keeps a second run from duplicating anything.

        if (_contentTypeService.Get(ContentTypeAlias) is not null)
        {
            _logger.LogInformation("Demo site already installed, skipping.");
            return;
        }

        try
        {
            ITemplate template = await CreateTemplateAsync();
            IContentType contentType = await CreateContentTypeAsync(template);
            await CreateHomePageAsync(contentType);
            _logger.LogInformation("Demo site installed: document type '{Alias}' and a published Home node.", ContentTypeAlias);
        }
        catch (Exception ex)
        {
            // A failure here must not take the site down - the backoffice still works.
            _logger.LogError(ex, "Failed to install the demo site.");
        }
    }

    private async Task<ITemplate> CreateTemplateAsync()
    {
        ITemplate? existing = await _templateService.GetAsync(TemplateAlias);
        if (existing is not null)
        {
            return existing;
        }

        Attempt<ITemplate, TemplateOperationStatus> attempt =
            await _templateService.CreateAsync("Home Page", TemplateAlias, HomePageView, Constants.Security.SuperUserKey);

        if (attempt.Success is false || attempt.Result is null)
        {
            throw new InvalidOperationException($"Could not create the template: {attempt.Status}.");
        }

        return attempt.Result;
    }

    private async Task<IContentType> CreateContentTypeAsync(ITemplate template)
    {
        IDataType textstring = await GetDataTypeAsync(TextstringDataType);
        IDataType richtext = await GetDataTypeAsync(RichtextDataType);

        var contentType = new ContentType(_shortStringHelper, Constants.System.Root)
        {
            Alias = ContentTypeAlias,
            Name = "Home Page",
            Description = "The front page of the demo site.",
            Icon = "icon-home color-green",
            AllowedAsRoot = true,
        };

        var properties = new PropertyTypeCollection(true, new[]
        {
            NewProperty(textstring, "title", "Title", "Big headline at the top of the page.", mandatory: true, sortOrder: 0),
            NewProperty(textstring, "subtitle", "Subtitle", "One supporting line under the headline.", mandatory: false, sortOrder: 1),
            NewProperty(richtext, "bodyText", "Body text", "Main content of the page.", mandatory: false, sortOrder: 2),
            NewProperty(textstring, "footerNote", "Footer note", "Small print at the bottom.", mandatory: false, sortOrder: 3),
        });

        contentType.PropertyGroups.Add(new PropertyGroup(properties)
        {
            Alias = "content",
            Name = "Content",
            Type = PropertyGroupType.Group,
            SortOrder = 0,
        });

        contentType.AllowedTemplates = new[] { template };
        contentType.SetDefaultTemplate(template);

        Attempt<ContentTypeOperationStatus> created =
            await _contentTypeService.CreateAsync(contentType, Constants.Security.SuperUserKey);

        if (created.Success is false)
        {
            throw new InvalidOperationException($"Could not create the document type: {created.Result}.");
        }

        return contentType;
    }

    private PropertyType NewProperty(IDataType dataType, string alias, string name, string description, bool mandatory, int sortOrder) =>
        new(_shortStringHelper, dataType, alias)
        {
            Name = name,
            Description = description,
            Mandatory = mandatory,
            SortOrder = sortOrder,
        };

    private async Task<IDataType> GetDataTypeAsync(Guid key) =>
        await _dataTypeService.GetAsync(key)
        ?? throw new InvalidOperationException($"Built-in data type {key} was not found.");

    private async Task CreateHomePageAsync(IContentType contentType)
    {
        IContent home = _contentService.Create("Home", Constants.System.Root, contentType.Alias);

        home.SetValue("title", "Umbraco is running in Docker");
        home.SetValue("subtitle", "This page, its document type and its template were all created from code on first boot.");
        home.SetValue("bodyText",
            "<p>Everything you see here lives in the CMS. Open the backoffice, change the text, hit save and publish, " +
            "then refresh this page - no rebuild needed.</p>" +
            "<p>The document type behind this page is <strong>Home Page</strong>, and it has four properties: " +
            "<em>Title</em>, <em>Subtitle</em>, <em>Body text</em> and <em>Footer note</em>.</p>");
        home.SetValue("footerNote", "Umbraco 17 LTS on .NET 10, SQLite, single container.");

        _contentService.Save(home);

        Attempt<ContentPublishingResult, ContentPublishingOperationStatus> published =
            await _contentPublishingService.PublishAsync(
                home.Key,
                new[] { new CulturePublishScheduleModel { Culture = Constants.System.InvariantCulture } },
                Constants.Security.SuperUserKey);

        if (published.Success is false)
        {
            throw new InvalidOperationException($"Could not publish the Home node: {published.Status}.");
        }
    }

    private const string HomePageView = """
@inherits Umbraco.Cms.Web.Common.Views.UmbracoViewPage
@{
    Layout = null;
    var title = Model.Value<string>("title");
    var subtitle = Model.Value<string>("subtitle");
    var bodyText = Model.Value("bodyText");
    var footerNote = Model.Value<string>("footerNote");
}
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>@title</title>
    <link rel="stylesheet" href="/css/demo.css" />
</head>
<body>
    <main class="page">
        <header class="hero">
            <p class="eyebrow">@Model.ContentType.Alias &middot; @Model.Name</p>
            <h1>@title</h1>
            @if (string.IsNullOrWhiteSpace(subtitle) == false)
            {
                <p class="lede">@subtitle</p>
            }
        </header>

        <section class="prose">
            @bodyText
        </section>

        <section class="facts">
            <dl>
                <div><dt>Document type</dt><dd>@Model.ContentType.Alias</dd></div>
                <div><dt>Published</dt><dd>@Model.UpdateDate.ToString("yyyy-MM-dd HH:mm")</dd></div>
                <div><dt>Node id</dt><dd>@Model.Id</dd></div>
            </dl>
        </section>

        <footer class="foot">
            <p>@footerNote</p>
            <p><a href="/umbraco">Open the backoffice &rarr;</a></p>
        </footer>
    </main>
</body>
</html>
""";
}
