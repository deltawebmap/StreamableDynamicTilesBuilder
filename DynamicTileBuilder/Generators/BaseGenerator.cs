using LibDeltaSystem.Db.System.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DynamicTileBuilder.Generators
{
    public abstract class BaseGenerator
    {
        public abstract int GetTile(DynamicTileTarget target, Stream outputStream);
    }
}
