using System.ServiceModel.Syndication;
using System.Xml;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using UnDotNet.HtmlToText;

namespace AnEoT.Api.Search;

[Route("search")]
[ApiController]
public class SearchController(IHttpClientFactory httpClientFactory, IMemoryCache cache) : ControllerBase
{
    [HttpGet("windows/{keyword}", Name = "Search for Windows")]
    public async Task<IResult> SearchForWindows(string keyword)
    {
        //IQueryCollection query = HttpContext.Request.Query;

        const string AtomCacheKey = "atom-full-string";

        if (!cache.TryGetValue(AtomCacheKey, out string? atomXml))
        {
            HttpClient client = httpClientFactory.CreateClient();
            atomXml = await client.GetStringAsync("https://aneot-vintage.arktca.com/atom_full.xml");

            MemoryCacheEntryOptions cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(TimeSpan.FromMinutes(30))
                .SetAbsoluteExpiration(TimeSpan.FromHours(1));
            // 30 分钟内没有访问则过期，1 小时后无论有人访问还是无人访问都过期

            cache.Set(AtomCacheKey, atomXml, cacheEntryOptions);
        }

        using StringReader stringReader = new(atomXml!);
        using XmlReader feedReader = XmlReader.Create(stringReader);

        HtmlToTextConverter htmlToTextConverter = new();
        HtmlToTextOptions htmlToTextOptions = new();
        // TODO: 不要图像输出
        //htmlToTextOptions.Img.Options.LinkBrackets = null;

        SyndicationFeed feed = SyndicationFeed.Load(feedReader);
        SyndicationFeed result = feed.Clone(false);

        result.Title = new TextSyndicationContent("回归线搜索");
        result.Id = "AnEoT-Search";
        result.Generator = "System.ServiceModel.Syndication.SyndicationFeed, used in AnEoT Search";

        List<SyndicationItem> items = new(feed.Items.Count());

        foreach (SyndicationItem item in feed.Items)
        {
            string content = item.Content switch
            {
                TextSyndicationContent text => htmlToTextConverter.Convert(text.Text, htmlToTextOptions),
                _ => string.Empty,
            };

            bool appearInAuthors = item.Authors.Any(person => person.Name is not null && person.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            bool appearInTitle = item.Title.Text.Contains(keyword, StringComparison.OrdinalIgnoreCase);
            bool appearInContent = content.Contains(keyword, StringComparison.OrdinalIgnoreCase);

            if (appearInAuthors || appearInTitle || appearInContent)
            {
                SyndicationItem newItem = item.Clone();

                const int startingSpaceLength = 10;
                const int endingSpaceLength = 60;

                string newContent;
                if (content.Length > endingSpaceLength)
                {
                    if (appearInContent)
                    {
                        int keywordIndex = content.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);

                        bool hasEnoughStartingSpace = keywordIndex - startingSpaceLength >= 0;
                        bool hasEnoughEndingSpace = keywordIndex + endingSpaceLength < content.Length;

                        string startingSpaceContent = hasEnoughStartingSpace
                            ? $"......{content[(keywordIndex - startingSpaceLength)..keywordIndex]}"
                            : string.Empty;

                        newContent = hasEnoughEndingSpace
                            ? $"{startingSpaceContent}{content[keywordIndex..(keywordIndex + endingSpaceLength)]}......"
                            : $"{startingSpaceContent}{content[keywordIndex..]}";
                    }
                    else
                    {
                        newContent = content;
                    }
                }
                else
                {
                    newContent = content;
                }

                newItem.Content = SyndicationContent.CreatePlaintextContent(newContent);

                items.Add(newItem);
            }
        }

        //if (query.TryGetValue("start", out var startIndexStr)
        //    && int.TryParse(startIndexStr.ToString(), out int startIndex)
        //    && items.Count != 0)
        //{
        //    if (startIndex - 1 >= 0)
        //    {
        //        startIndex--;
        //    }

        //    if (!query.TryGetValue("count", out var countString)
        //        || !int.TryParse(startIndexStr.ToString(), out int loadCount)
        //        || loadCount <= 0)
        //    {
        //        loadCount = 20;
        //    }

        //    if (startIndex + loadCount < items.Count)
        //    {
        //        result.Items = items[startIndex..(startIndex + loadCount)];
        //    }
        //    else
        //    {
        //        result.Items = items[startIndex..];
        //    }
        //}
        //else
        //{
        //    result.Items = items;
        //}

        result.Items = items;

        return TypedResults.Stream(stream =>
        {
            Atom10FeedFormatter feedFormatter = new(result);
            XmlWriterSettings settings = new()
            {
                NewLineHandling = NewLineHandling.Entitize,
                NewLineOnAttributes = true,
                Indent = true
            };

            // System.ServiceModel.Syndication.SyndicationFeed 还不支持异步读写
            IHttpBodyControlFeature? syncIOFeature = HttpContext.Features.Get<IHttpBodyControlFeature>();
            if (syncIOFeature != null)
            {
                syncIOFeature.AllowSynchronousIO = true;
            }

            using XmlWriter xmlWriter = XmlWriter.Create(stream, settings);
            feedFormatter.WriteTo(xmlWriter);

            return Task.CompletedTask;
        }, contentType: "application/xml");
    }
}
