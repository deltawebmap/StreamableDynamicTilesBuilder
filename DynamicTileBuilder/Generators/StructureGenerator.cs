using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ArkSaveEditor.ArkEntries;
using ArkSaveEditor.Entities;
using LibDeltaSystem.Db.Content;
using LibDeltaSystem.Db.System.Entities;
using LibDeltaSystem.Entities.DynamicTiles;
using MongoDB.Driver;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace DynamicTileBuilder.Generators
{
    public class StructureGenerator : BaseGenerator
    {
        public const int MIN_RESIZED_SIZE = 3;

        public override int GetTile(DynamicTileTarget target, Stream outputStream)
        {
            //Get map data
            ArkMapData mapinfo = Program.ark_maps[target.map_name];

            //Get tile info
            TileData tile = target.GetTileData(mapinfo.captureSize);

            //Set up vars
            int resolution = 256;
            float tilePpm = resolution / tile.units_per_tile; //Pixels per meter

            //Get tiles
            List<DbStructure> structures = GetTilesInRange(target, tile).GetAwaiter().GetResult();

            //Find all tiles
            List<QueuedTile> tilesInRange = new List<QueuedTile>();
            foreach (var t in structures)
            {
                //Get structure metadata
                var displayMetadata = Program.structure_metadata.Where(x => x.names.Contains(t.classname)).FirstOrDefault();
                if (displayMetadata == null)
                    continue;

                //Check if image exists
                if (!Program.image_package.images["structure"].ContainsKey(displayMetadata.img + ".png"))
                    continue;

                //Get image and it's width and height in game units
                Image<Rgba32> img = Program.image_package.images["structure"][displayMetadata.img + ".png"];
                float ppm = displayMetadata.capturePixels / displayMetadata.captureSize;
                float img_game_width = img.Width * ppm;
                float img_game_height = img.Height * ppm;

                //Check if it is in range
                if (t.location.x > tile.game_max_x + (img_game_width) || t.location.x < tile.game_min_x - (img_game_width))
                    continue;
                if (t.location.y > tile.game_max_y + (img_game_height) || t.location.y < tile.game_min_y - (img_game_height))
                    continue;

                //Determine size
                float scaleFactor = tilePpm / ppm;
                int img_scale_x = (int)(img.Width * scaleFactor);
                int img_scale_y = (int)(img.Height * scaleFactor);

                //Check if this'll even be big enough to see
                if (img_scale_x < MIN_RESIZED_SIZE || img_scale_y < MIN_RESIZED_SIZE)
                    continue;

                //Create
                Image<Rgba32> source = Program.image_package.images["structure"][displayMetadata.img + ".png"];
                var simg = source.Clone();
                simg.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new SixLabors.Primitives.Size(img_scale_x, img_scale_y),
                    Position = AnchorPositionMode.Center
                }).Rotate(t.location.yaw));

                //Determine location
                float loc_tile_x = (t.location.x - (img_game_width / 2) - tile.game_min_x) / tile.units_per_tile; //Top left of the image inside the tile
                float loc_tile_y = (t.location.y - (img_game_height / 2) - tile.game_min_y) / tile.units_per_tile; //Top left of the image inside the tile
                int copy_offset_x = (int)(loc_tile_x * resolution);
                int copy_offset_y = (int)(loc_tile_y * resolution);

                //Add to list
                tilesInRange.Add(new QueuedTile
                {
                    img = simg,
                    copy_offset_x = copy_offset_x,
                    copy_offset_y = copy_offset_y,
                    z = t.location.z,
                    display_type = displayMetadata.displayType
                });
            }

            //If there are no tiles, do not continue
            if (tilesInRange.Count == 0)
                return 0;

            //Sort by Y level
            tilesInRange.Sort(new Comparison<QueuedTile>((x, y) =>
            {
                if (x.display_type == StructureDisplayMetadata_DisplayType.AlwaysTop || y.display_type == StructureDisplayMetadata_DisplayType.AlwaysTop)
                    return x.display_type.CompareTo(y.display_type);
                return x.z.CompareTo(y.z);
            }));

            //Now, copy the images
            Image<Rgba32> output = new Image<Rgba32>(resolution, resolution);
            foreach (var q in tilesInRange)
            {
                //Copy
                for (int x = 0; x < q.img.Width; x++)
                {
                    for (int y = 0; y < q.img.Height; y++)
                    {
                        //Check if this is just alpha
                        if (q.img[x, y].A == 0)
                            continue;

                        //Check if in range
                        if (x + q.copy_offset_x < 0 || y + q.copy_offset_y < 0)
                            continue;
                        if (x + q.copy_offset_x >= output.Width || y + q.copy_offset_y >= output.Height)
                            continue;

                        //Mix color
                        Rgba32 sourcePixel = output[x + q.copy_offset_x, y + q.copy_offset_y];
                        Rgba32 edgePixel = q.img[x, y];
                        float edgeMult = ((float)edgePixel.A) / 255;
                        float sourceMult = 1 - edgeMult;
                        Rgba32 mixedColor = new Rgba32(
                            ((((float)edgePixel.R) / 255) * edgeMult) + ((((float)sourcePixel.R) / 255) * sourceMult),
                            ((((float)edgePixel.G) / 255) * edgeMult) + ((((float)sourcePixel.G) / 255) * sourceMult),
                            ((((float)edgePixel.B) / 255) * edgeMult) + ((((float)sourcePixel.B) / 255) * sourceMult),
                            ((((float)edgePixel.A) / 255) * edgeMult) + ((((float)sourcePixel.A) / 255) * sourceMult)
                        );
                        output[x + q.copy_offset_x, y + q.copy_offset_y] = mixedColor;
                    }
                }
            }

            //Now, we'll save
            output.SaveAsPng(outputStream, new SixLabors.ImageSharp.Formats.Png.PngEncoder
            {
                CompressionLevel = 9
            });

            return tilesInRange.Count;
        }

        private static async Task<List<DbStructure>> GetTilesInRange(DynamicTileTarget target, TileData tile)
        {
            //Get the sizes. These offer an OK estimation of overlap
            float tileSizeX = tile.game_max_x - tile.game_min_x;
            float tileSizeY = tile.game_max_y - tile.game_min_y;

            //Commit query
            var filterBuilder = Builders<DbStructure>.Filter;
            var filter = filterBuilder.Eq("server_id", target.server_id.ToString()) &
                filterBuilder.Eq("tribe_id", target.tribe_id) &
                filterBuilder.In("classname", Program.compatible_structure_classnames) &
                filterBuilder.Gt("location.x", tile.game_min_x - tileSizeX) &
                filterBuilder.Lt("location.x", tile.game_max_x + tileSizeX) &
                filterBuilder.Gt("location.y", tile.game_min_y - tileSizeY) &
                filterBuilder.Lt("location.y", tile.game_max_y + tileSizeY);
            var results = await Program.conn.content_structures.FindAsync(filter);
            return await results.ToListAsync();
        }

        class QueuedTile
        {
            public int copy_offset_x;
            public int copy_offset_y;
            public float z;
            public Image<Rgba32> img;
            public StructureDisplayMetadata_DisplayType display_type;
        }
    }
}
