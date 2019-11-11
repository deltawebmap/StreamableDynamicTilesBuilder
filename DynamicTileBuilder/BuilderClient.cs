using System;
using System.Collections.Generic;
using System.Net;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using DynamicTileBuilder.Generators;
using LibDeltaSystem;
using LibDeltaSystem.Tools;
using LibDeltaSystem.Entities.DynamicTiles;
using LibDeltaSystem.Tools.InternalComms;
using Newtonsoft.Json;

namespace DynamicTileBuilder
{
    public class BuilderClient : InternalCommClient
    {
        public BuilderClient(DeltaConnection conn, byte[] key, IPEndPoint endpoint) : base(conn, key, endpoint)
        {

        }

        public override async Task HandleMessage(int opcode, Dictionary<string, byte[]> payloads)
        {
            try
            {
                if (opcode == 0)
                    BuildTile(GetJsonFromPayload<DynamicTileBuilderRequest>(payloads, "REQUEST"));
            } catch (Exception ex)
            {
                Console.WriteLine(ex.Message + ex.StackTrace);
            }
        }

        /// <summary>
        /// Starts to render a tile.
        /// </summary>
        /// <param name="request"></param>
        public void BuildTile(DynamicTileBuilderRequest request)
        {
            //Get the generator
            BaseGenerator generator = GeneratorHolder.GetGenerator(request.target.map_id);

            //Process file and make response
            string url;
            int tiles;
            using (MemoryStream ms = new MemoryStream())
            {
                //Process
                tiles = generator.GetTile(request.target, ms);
                if(tiles == 0)
                {
                    url = "https://" + Program.server_name + "/blank";
                } else
                {
                    //Generate a random file ID and open a stream on it
                    string name = SecureStringTool.GenerateSecureString(32);
                    while (File.Exists(Program.config.builder_output + name))
                        name = SecureStringTool.GenerateSecureString(32);

                    //Write file
                    ms.Position = 0;
                    using (FileStream fs = new FileStream(Program.config.builder_output + name, FileMode.Create))
                        ms.CopyTo(fs);
                    url = "https://" + Program.server_name + "/t/" + name;
                }
            }

            //Create response message
            DynamicTileBuilderResponse response = new DynamicTileBuilderResponse
            {
                ok = true,
                ready = true,
                server = Program.server_name,
                url = url,
                target = request.target,
                count = tiles,
                revision_id = request.structure_revision_id
            };

            //Send response message
            RawSendMessage(1, new Dictionary<string, byte[]>
            {
                {"RESPONSE", Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response)) }
            });
        }
    }
}
