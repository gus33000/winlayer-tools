using Flurl.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WinLayerLinkGen
{
    public class TagsList
    {
        public string name { get; set; }
        public List<string> tags { get; set; }
    }

    public class Config
    {
        public string mediaType { get; set; }
        public int size { get; set; }
        public string digest { get; set; }
    }

    public class Layer
    {
        public string mediaType { get; set; }
        public long size { get; set; }
        public string digest { get; set; }
        public List<string> urls { get; set; }
    }

    public class Manifest
    {
        public int schemaVersion { get; set; }
        public string mediaType { get; set; }
        public Config config { get; set; }
        public List<Layer> layers { get; set; }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            string baseurl = @"https://mcr.microsoft.com/v2/windows/insider";
            if (args.Count() > 0)
            {
                baseurl = args[0].Replace("/tags/list", "", StringComparison.InvariantCultureIgnoreCase)
                    .Replace("/manifests/", "", StringComparison.InvariantCultureIgnoreCase)
                    .Replace("/manifests", "", StringComparison.InvariantCultureIgnoreCase);
            }

            string url = $"{baseurl}/tags/list";
            string url2 = $"{baseurl}/manifests/";

            TagsList list = await url.GetJsonAsync<TagsList>();
            Console.WriteLine(list.name);
            foreach (var tagname in list.tags)
            {
                Console.Title = tagname;
                try
                {
                    string url3 = url2 + tagname;
                    Manifest man = await url3.WithHeader("Accept", "application/vnd.docker.distribution.manifest.v2+json").GetJsonAsync<Manifest>();
                    Console.WriteLine(tagname);
                    Console.WriteLine(man.layers[0].urls[0]);
                    Console.WriteLine();
                }
                catch { }
            }
        }
    }
}
