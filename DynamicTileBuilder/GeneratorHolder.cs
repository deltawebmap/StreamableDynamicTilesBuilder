using DynamicTileBuilder.Generators;
using LibDeltaSystem.Db.System.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace DynamicTileBuilder
{
    public static class GeneratorHolder
    {
        private static readonly Dictionary<DynamicTileType, BaseGenerator> generators = new Dictionary<DynamicTileType, BaseGenerator>
        {
            {DynamicTileType.StructureImages, new StructureGenerator() }
        };

        public static BaseGenerator GetGenerator(DynamicTileType type)
        {
            return generators[type];
        }
    }
}
