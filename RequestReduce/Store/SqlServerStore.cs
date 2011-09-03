﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using RequestReduce.Utilities;

namespace RequestReduce.Store
{
    public class SqlServerStore : IStore
    {
        private readonly IUriBuilder uriBuilder;
        private readonly IFileRepository repository;
        private readonly IStore fileStore;
        public event DeleeCsAction CssDeleted;
        public event AddCssAction CssAded;

        public SqlServerStore(IUriBuilder uriBuilder, IFileRepository repository, IStore fileStore)
        {
            RRTracer.Trace("Sql Server Store Created.");
            this.uriBuilder = uriBuilder;
            this.repository = repository;
            this.fileStore = fileStore;
        }

        public void Flush(Guid keyGuid)
        {
            if(keyGuid == Guid.Empty)
            {
                var urls = GetSavedUrls();
                foreach (var key in urls.Keys)
                    Flush(key);
            }

            var files = repository.GetFilesFromKey(keyGuid);
            foreach (var file in files)
            {
                file.IsExpired = true;
                repository.Save(file);
            }
            if (CssDeleted != null)
                CssDeleted(keyGuid);
        }

        public void Dispose()
        {
            fileStore.Dispose();
            RRTracer.Trace("Sql Server Store Disposed.");
        }

        public void Save(byte[] content, string url, string originalUrls)
        {
            RRTracer.Trace("Saving {0} to db.", url);
            var fileName = uriBuilder.ParseFileName(url);
            var key = uriBuilder.ParseKey(url);
            var id = Guid.Parse(uriBuilder.ParseSignature(url));
            var file = repository[id] ?? new RequestReduceFile();
            file.Content = content;
            file.LastUpdated = DateTime.Now;
            file.FileName = fileName;
            file.Key = key;
            file.RequestReduceFileId = id;
            file.OriginalName = originalUrls;
            file.IsExpired = false;
            fileStore.Save(content, url, originalUrls);
            if(CssAded != null && !url.ToLower().EndsWith(".png"))
                CssAded(key, url);
            else
                RRTracer.Trace("Repository is not bound to store.");
            repository.Save(file);
            RRTracer.Trace("{0} saved to db.", url);
        }

        public bool SendContent(string url, HttpResponseBase response)
        {
            if (fileStore.SendContent(url, response))
                return true;

            var key = uriBuilder.ParseKey(url);
            var id = Guid.Parse(uriBuilder.ParseSignature(url));
            var file = repository[id];

            if(file != null)
            {
                response.BinaryWrite(file.Content);
                fileStore.Save(file.Content, url, null);
                RRTracer.Trace("{0} transmitted from db.", url);
                if (file.IsExpired && CssDeleted != null)
                    CssDeleted(key);
                return true;
            }

            RRTracer.Trace("{0} not found on file or db.", url);
            if (CssDeleted != null)
                CssDeleted(key);
            return false;
        }

        public IDictionary<Guid, string> GetSavedUrls()
        {
            RRTracer.Trace("SqlServerStore Looking for previously saved content.");
            var files = repository.GetActiveCssFiles();
            return files.ToDictionary(file => uriBuilder.ParseKey(file), file => uriBuilder.BuildCssUrl(uriBuilder.ParseKey(file), uriBuilder.ParseSignature(file)));
        }
    }
}
