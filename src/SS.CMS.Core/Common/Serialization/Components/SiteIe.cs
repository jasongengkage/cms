﻿using System;
using System.Threading.Tasks;
using SS.CMS.Core.Models.Attributes;
using SS.CMS.Enums;
using SS.CMS.Models;
using SS.CMS.Repositories;
using SS.CMS.Services;
using SS.CMS.Utils;
using SS.CMS.Utils.Atom.Atom.Core;

namespace SS.CMS.Core.Serialization.Components
{
    internal class SiteIe
    {
        private readonly Site _siteInfo;
        private readonly string _siteContentDirectoryPath;
        private readonly ChannelIe _channelIe;
        private readonly ContentIe _contentIe;
        private readonly IPluginManager _pluginManager;
        private readonly IPathManager _pathManager;
        private readonly ISiteRepository _siteRepository;
        private readonly IChannelRepository _channelRepository;

        public SiteIe(Site siteInfo, string siteContentDirectoryPath)
        {
            _siteContentDirectoryPath = siteContentDirectoryPath;
            _siteInfo = siteInfo;
            _channelIe = new ChannelIe(siteInfo);
            _contentIe = new ContentIe(siteInfo, siteContentDirectoryPath);
        }

        public async Task<int> ImportChannelsAndContentsAsync(string filePath, bool isImportContents, bool isOverride, int theParentId, int userId)
        {
            var psChildCount = await _channelRepository.GetCountAsync(_siteInfo.Id);
            var indexNameList = await _channelRepository.GetIndexNameListAsync(_siteInfo.Id);

            if (!FileUtils.IsFileExists(filePath)) return 0;
            var feed = AtomFeed.Load(FileUtils.GetFileStreamReadOnly(filePath));

            var firstIndex = filePath.LastIndexOf(PathUtils.SeparatorChar) + 1;
            var lastIndex = filePath.LastIndexOf(".", StringComparison.Ordinal);
            var orderString = filePath.Substring(firstIndex, lastIndex - firstIndex);

            var idx = orderString.IndexOf("_", StringComparison.Ordinal);
            if (idx != -1)
            {
                var secondOrder = TranslateUtils.ToInt(orderString.Split('_')[1]);
                secondOrder = secondOrder + psChildCount;
                orderString = orderString.Substring(idx + 1);
                idx = orderString.IndexOf("_", StringComparison.Ordinal);
                if (idx != -1)
                {
                    orderString = orderString.Substring(idx);
                    orderString = "1_" + secondOrder + orderString;
                }
                else
                {
                    orderString = "1_" + secondOrder;
                }

                orderString = orderString.Substring(0, orderString.LastIndexOf("_", StringComparison.Ordinal));
            }

            var parentId = await _channelRepository.GetIdAsync(_siteInfo.Id, orderString);
            if (theParentId != 0)
            {
                parentId = theParentId;
            }

            var parentIdOriginal = TranslateUtils.ToInt(AtomUtility.GetDcElementContent(feed.AdditionalElements, ChannelAttribute.ParentId));
            int channelId;
            if (parentIdOriginal == 0)
            {
                channelId = _siteInfo.Id;
                var nodeInfo = await _channelRepository.GetChannelInfoAsync(_siteInfo.Id);
                await _channelIe.ImportNodeInfoAsync(nodeInfo, feed.AdditionalElements, parentId, indexNameList);

                await _channelRepository.UpdateAsync(nodeInfo);

                if (isImportContents)
                {
                    await _contentIe.ImportContentsAsync(feed.Entries, nodeInfo, 0, isOverride, userId);
                }
            }
            else
            {
                var nodeInfo = new Channel();
                await _channelIe.ImportNodeInfoAsync(nodeInfo, feed.AdditionalElements, parentId, indexNameList);
                if (string.IsNullOrEmpty(nodeInfo.ChannelName)) return 0;

                var isUpdate = false;
                var theSameNameChannelId = 0;
                if (isOverride)
                {
                    theSameNameChannelId = await _channelRepository.GetChannelIdByParentIdAndChannelNameAsync(_siteInfo.Id, parentId, nodeInfo.ChannelName, false);
                    if (theSameNameChannelId != 0)
                    {
                        isUpdate = true;
                    }
                }
                if (!isUpdate)
                {
                    channelId = await _channelRepository.InsertAsync(nodeInfo);
                }
                else
                {
                    channelId = theSameNameChannelId;
                    nodeInfo = await _channelRepository.GetChannelInfoAsync(theSameNameChannelId);
                    var tableName = await _channelRepository.GetTableNameAsync(_pluginManager, _siteInfo, nodeInfo);
                    await _channelIe.ImportNodeInfoAsync(nodeInfo, feed.AdditionalElements, parentId, indexNameList);

                    await _channelRepository.UpdateAsync(nodeInfo);

                    //DataProvider.ContentDao.DeleteContentsByChannelId(_siteInfo.Id, tableName, theSameNameChannelId);
                }

                if (isImportContents)
                {
                    await _contentIe.ImportContentsAsync(feed.Entries, nodeInfo, 0, isOverride, userId);
                }
            }

            return channelId;
        }

        public async Task ExportAsync(int siteId, int channelId, bool isSaveContents)
        {
            var channelInfo = await _channelRepository.GetChannelInfoAsync(channelId);
            if (channelInfo == null) return;

            var siteInfo = await _siteRepository.GetSiteInfoAsync(siteId);

            var fileName = await _channelRepository.GetOrderStringInSiteAsync(channelId);

            var filePath = _siteContentDirectoryPath + PathUtils.SeparatorChar + fileName + ".xml";

            var feed = await _channelIe.ExportNodeInfoAsync(channelInfo);

            if (isSaveContents)
            {
                var contentIdList = await channelInfo.ContentRepository.GetContentIdListCheckedAsync(channelId, TaxisType.OrderByTaxis);
                foreach (var contentId in contentIdList)
                {
                    var contentInfo = await channelInfo.ContentRepository.GetContentInfoAsync(contentId);
                    //ContentUtility.PutImagePaths(siteInfo, contentInfo as BackgroundContentInfo, collection);
                    var entry = _contentIe.ExportContentInfo(contentInfo);
                    feed.Entries.Add(entry);

                }
            }
            feed.Save(filePath);

            //  foreach (string imageUrl in collection.Keys)
            //  {
            //     string sourceFilePath = collection[imageUrl];
            //     string destFilePath = PathUtility.MapPath(this.siteContentDirectoryPath, imageUrl);
            //     DirectoryUtils.CreateDirectoryIfNotExists(destFilePath);
            //     FileUtils.MoveFile(sourceFilePath, destFilePath, true);
            //  }
        }
    }
}
