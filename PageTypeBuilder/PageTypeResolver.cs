using System;
using System.Collections.Generic;
using System.Web;
using System.Web.Caching;
using System.Web.Profile;
using EPiServer;
using EPiServer.Core;
using log4net;
using log4net.Repository.Hierarchy;
using EPiServer.Configuration;
using PageTypeBuilder.Activation;

namespace PageTypeBuilder
{
    public class PageTypeResolver
    {
        private Dictionary<int, Type> _typeByPageTypeID = new Dictionary<int, Type>();
        private Dictionary<Type, int> _pageTypeIDByType = new Dictionary<Type, int>();
        private ILog _logger = LogManager.GetLogger("PageTypeResolver");

        private static PageTypeResolver _instance;

        protected internal PageTypeResolver()
        {
            Activator = new TypedPageActivator();
        }

        protected internal virtual void AddPageType(int pageTypeID, Type pageTypeType)
        {
            if(AlreadyAddedToTypeByPageTypeID(pageTypeID, pageTypeType))
                _typeByPageTypeID.Add(pageTypeID, pageTypeType);

            if(AlreadyAddedToPageTypeIDByType(pageTypeID, pageTypeType))
                _pageTypeIDByType.Add(pageTypeType, pageTypeID);
        }

        private bool AlreadyAddedToTypeByPageTypeID(int pageTypeID, Type pageTypeType)
        {
            return !_typeByPageTypeID.ContainsKey(pageTypeID) || _typeByPageTypeID[pageTypeID] != pageTypeType;
        }

        private bool AlreadyAddedToPageTypeIDByType(int pageTypeID, Type pageTypeType)
        {
            return !_pageTypeIDByType.ContainsKey(pageTypeType) || _pageTypeIDByType[pageTypeType] != pageTypeID;
        }

        public virtual Type GetPageTypeType(int pageTypeID)
        {
            Type type = null;

            if (_typeByPageTypeID.ContainsKey(pageTypeID))
            {
                type = _typeByPageTypeID[pageTypeID];
            }

            return type;
        }

        public virtual int? GetPageTypeID(Type type)
        {
            int? pageTypeID = null;

            if (_pageTypeIDByType.ContainsKey(type))
            {
                pageTypeID = _pageTypeIDByType[type];
            }

            return pageTypeID;
        }

        public virtual PageData ConvertToTyped(PageData page)
        {
            Type type = GetPageTypeType(page.PageTypeID);
            var castingTest = page as TypedPageData;

            if (type == null || castingTest != null)
            {
                if (type != null)
                {
                    if(_logger.IsDebugEnabled)
                        _logger.Debug(string.Format("Cache hit on {0}", type.Name));
                }
                    
                return page;
            }
                
            var populated = Activator.CreateAndPopulateTypedInstance(page, type);

            if (page.WorkPageID != 0)
            {
                _logger.Debug(string.Format("Skipping page with work ID {0}", page.WorkPageID));
                return populated;
            }

            // Save to cache if its a final published version 
            var pageProvider = DataFactory.Instance.GetPageProvider(page.PageLink);
            var cacheSettings = new CacheSettings(PageCacheTimeout);

            if (_logger.IsDebugEnabled)
                _logger.Debug(string.Format("Saving to cache {0}", type.Name));

            string key = DataFactoryCache.PageCommonCacheKey(page.PageLink);
            string str2 = DataFactoryCache.PageLanguageCacheKey(page.PageLink, page.LanguageBranch);

            if (cacheSettings.CancelCaching)
            {
                CacheManager.RemoveLocalOnly(key);
            }
            else
            {
                if (CacheManager.RuntimeCacheGet(key) == null)
                {
                    CacheManager.RuntimeCacheInsert(key, DateTime.UtcNow.Ticks, new CacheDependency(null, new string[] { "DataFactoryCache.MasterKey", pageProvider.ProviderCacheKey }), Cache.NoAbsoluteExpiration, PageCacheTimeout);
                }
                string[] filenames = (cacheSettings.FileNames.Count > 0) ? cacheSettings.FileNames.ToArray() : null;
                List<string> list = new List<string>(new string[] { "DataFactoryCache.MasterKey", pageProvider.ProviderCacheKey, key });
                list.AddRange(cacheSettings.CacheKeys);
                if (page.IsMasterLanguageBranch)
                {
                    CacheManager.RuntimeCacheInsert(DataFactoryCache.PageMasterLanguageCacheKey(page.PageLink), populated, new CacheDependency(filenames, list.ToArray()), cacheSettings.AbsoluteExpiration, cacheSettings.SlidingExpiration);
                }
                CacheManager.RuntimeCacheInsert(str2, populated, new CacheDependency(filenames, list.ToArray()), cacheSettings.AbsoluteExpiration, cacheSettings.SlidingExpiration);
            }

            return populated;
        }

        private TimeSpan? _pageCacheTimeout;
        private TimeSpan PageCacheTimeout
        {
            get
            {
                if (!_pageCacheTimeout.HasValue)
                {
                    _pageCacheTimeout = new TimeSpan?(Settings.Instance.PageCacheSlidingExpiration);
                }
                return _pageCacheTimeout.Value;
            }
        }

        public static PageTypeResolver Instance
        {
            get
            {
                if(_instance == null)
                    _instance = new PageTypeResolver();

                return _instance;
            }

            internal set
            {
                _instance = value;
            }
        }

        public TypedPageActivator Activator { get; set; }
    }
}
