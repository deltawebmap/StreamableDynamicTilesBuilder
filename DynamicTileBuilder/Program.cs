using ArkSaveEditor.ArkEntries;
using DynamicTileBuilder.Entities;
using LibDeltaSystem;
using Newtonsoft.Json;
using StreamableDynamicTiles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.IO.Compression;
using System.Text;

namespace DynamicTileBuilder
{
    class Program
    {
        public static DeltaConnection conn;
        public static ServiceConfig config;
        public static BuilderClient client;

        public static string server_name = "dynamic-tiles.deltamap.net";

        public static List<StructureDisplayMetadata> structure_metadata;
        public static PrimalDataImagePackage image_package;
        public static Dictionary<string, ArkSaveEditor.Entities.ArkMapData> ark_maps;
        public static List<string> compatible_structure_classnames;

        static void Main(string[] args)
        {
            //Load config
            config = new ServiceConfig();

            //Load Ark maps
            ark_maps = JsonConvert.DeserializeObject<Dictionary<string, ArkSaveEditor.Entities.ArkMapData>>(File.ReadAllText(config.map_config_file));

            //Import content
            image_package = ImportImages(config.image_content_path);
            structure_metadata = JsonConvert.DeserializeObject<List<StructureDisplayMetadata>>(File.ReadAllText(config.metadata_content_path));
            compatible_structure_classnames = new List<string>();
            foreach (var s in structure_metadata)
                compatible_structure_classnames.AddRange(s.names);

            //Connect to database
            conn = new DeltaConnection(config.database_config, "dynamic-tile-builder", 0, 0);
            conn.Connect().GetAwaiter().GetResult();

            //Start server
            client = new BuilderClient(conn, new byte[32], new IPEndPoint(IPAddress.Parse(config.builder_ip), config.builder_port));
            client.Connect();

            //Hang this thread
            Console.WriteLine("Ready");
            Task.Delay(-1).GetAwaiter().GetResult();
        }

        static PrimalDataImagePackage ImportImages(string pathname)
        {
            //Open stream and begin reading
            PrimalDataImagePackage package;
            using (FileStream fs = new FileStream(pathname, System.IO.FileMode.Open, FileAccess.Read))
            {
                using (ZipArchive za = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    //Read metadata
                    PrimalDataImagesMetadata meta = ReadEntryAsJson<PrimalDataImagesMetadata>("package_metadata.json", za);

                    //Open images
                    package = new PrimalDataImagePackage();
                    package.images = new Dictionary<string, Dictionary<string, Image<Rgba32>>>();
                    foreach (var type in meta.data)
                    {
                        Dictionary<string, Image<Rgba32>> imgs = new Dictionary<string, Image<Rgba32>>();
                        foreach (var i in type.Value)
                        {
                            //Read image
                            Image<Rgba32> source;
                            using (Stream s = za.GetEntry(i.Value).Open())
                                source = Image.Load<Rgba32>(s);

                            //Resize to square
                            int size = Math.Max(source.Width, source.Height);
                            Image<Rgba32> img = new Image<Rgba32>(size, size);
                            int offsetX = (size - source.Width) / 2;
                            int offsetY = (size - source.Height) / 2;
                            for (int x = 0; x < source.Width; x++)
                            {
                                for (int y = 0; y < source.Height; y++)
                                {
                                    img[x + offsetX, y + offsetY] = source[x, y];
                                }
                            }

                            //add
                            imgs.Add(i.Key, img);
                        }
                        package.images.Add(type.Key, imgs);
                    }
                }
            }
            return package;
        }

        public static T ReadEntryAsJson<T>(string name, ZipArchive z)
        {
            var entry = z.GetEntry(name);
            byte[] buf = new byte[entry.Length];
            using (Stream s = entry.Open())
            {
                s.Read(buf, 0, buf.Length);
            }
            return JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(buf));
        }
    }
}
