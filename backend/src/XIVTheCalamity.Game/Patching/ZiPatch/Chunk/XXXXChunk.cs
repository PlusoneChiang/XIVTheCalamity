using System.IO;

namespace XIVTheCalamity.Game.Patching.ZiPatch.Chunk
{
    // ReSharper disable once InconsistentNaming
    public class XXXXChunk : ZiPatchChunk
    {
        // TODO: This... Never happens.
        public new static string Type = "XXXX";
        public override string ChunkType => Type;

        protected override void ReadChunk()
        {
            using var advanceAfter = this.GetAdvanceOnDispose();
        }

        public XXXXChunk(BinaryReader reader, long offset, long size) : base(reader, offset, size) {}

        public override string ToString()
        {
            return Type;
        }
    }
}
