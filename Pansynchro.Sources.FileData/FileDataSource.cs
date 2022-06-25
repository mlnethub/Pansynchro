﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

using Pansynchro.Core;
using Pansynchro.Core.Connectors;

namespace Pansynchro.Sources.Files
{
    record FileSpec(string Name, string[] File);

    public class FileDataSource : IDataSource
    {
        private readonly FileSpec[] _conn;

        public FileDataSource(string connectionString)
        {
            var json = JObject.Parse(connectionString);
            var files = json["Files"] as JArray;
            if (files == null) {
                throw new ArgumentException("Config JSON is missing the Files property");
            }
            var list = files.ToObject<FileSpec[]>();
            if (list == null) {
                throw new ArgumentException("JSON Files property does not match the spec");
            }
            _conn = list;
        }

        private IEnumerable<(string name, string filename)> GetFilenames()
        {
            foreach (var (name, specs) in _conn) {
                foreach (var spec in specs) {
                    foreach (var filename in new FileSet(spec).Files) {
                        yield return (name, filename);
                    }
                }
            }
        }

        public async IAsyncEnumerable<(string name, Stream data)> GetDataAsync()
        {
            foreach (var (name, filename) in GetFilenames()) {
                yield return (name, File.OpenRead(filename));
            }
            await Task.CompletedTask; //just here to shut the compiler up
        }

        public async IAsyncEnumerable<Stream> GetDataAsync(string name)
        {
            foreach (var (lName, filename) in GetFilenames()) {
                if (lName == name) {
                    yield return File.OpenRead(filename);
                }
            }
            await Task.CompletedTask;  //just here to shut the compiler up
        }

        public async IAsyncEnumerable<(string name, TextReader data)> GetTextAsync()
        {
            foreach (var (name, filename) in GetFilenames()) {
                yield return (name, File.OpenText(filename));
            }
            await Task.CompletedTask; //just here to shut the compiler up
        }

        public async IAsyncEnumerable<TextReader> GetTextAsync(string name)
        {
            foreach (var (lName, filename) in GetFilenames()) {
                if (lName == name) {
                    yield return File.OpenText(filename);
                }
            }
            await Task.CompletedTask;  //just here to shut the compiler up
        }
    }
}
