using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Traffic.Art
{
    /// <summary>One model from the art file: a vehicle, a tree, a rock.</summary>
    public sealed class ArtModel
    {
        public string Name;
        /// <summary>How big it is, in meters: x is its length, y its height, z its width.</summary>
        public float SizeX, SizeY, SizeZ;
        /// <summary>Three numbers per corner. Vehicles face +x, stand on y = 0 and are centered.</summary>
        public float[] Positions, Normals;
        /// <summary>The color of each corner: a slot in the palette, or Paint or Leaf.</summary>
        public ushort[] Slots;
        /// <summary>For paint, how bright this part is next to the brightest (255).</summary>
        public byte[] Shades;
        /// <summary>Triangles, three corners each: those that take the vehicle's own color, and the rest.</summary>
        public int[] Paint, Rest;

        public int Count => Slots.Length;
    }

    /// <summary>
    /// Reads Assets/Resources/Art/models.bytes, which Tools/BakeArt/bake.py writes from the
    /// source models in Art/. It has no Unity references, so a test can read the file too.
    /// </summary>
    public sealed class ArtFile
    {
        /// <summary>A corner that takes the vehicle's own color.</summary>
        public const ushort Paint = 65535;
        /// <summary>A corner that takes the color chosen for this tree's leaves.</summary>
        public const ushort Leaf = 65534;

        /// <summary>The fixed colors, three bytes each (red, green, blue).</summary>
        public byte[] Palette;
        public readonly List<ArtModel> Models = new List<ArtModel>();

        public int PaletteCount => Palette.Length / 3;

        public static ArtFile Read(byte[] data)
        {
            var file = new ArtFile();
            using (var r = new BinaryReader(new MemoryStream(data)))
            {
                if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "TART") throw new InvalidDataException("Not an art file.");
                int version = r.ReadInt32();
                if (version != 1) throw new InvalidDataException("Art file version " + version + " is not understood.");
                file.Palette = r.ReadBytes(r.ReadInt32() * 3);
                int models = r.ReadInt32();
                for (int m = 0; m < models; m++)
                {
                    var model = new ArtModel { Name = Encoding.ASCII.GetString(r.ReadBytes(r.ReadByte())) };
                    model.SizeX = r.ReadSingle();
                    model.SizeY = r.ReadSingle();
                    model.SizeZ = r.ReadSingle();
                    int n = r.ReadInt32();
                    model.Positions = new float[n * 3];
                    model.Normals = new float[n * 3];
                    model.Slots = new ushort[n];
                    model.Shades = new byte[n];
                    for (int i = 0; i < n; i++)
                    {
                        for (int k = 0; k < 3; k++) model.Positions[i * 3 + k] = r.ReadSingle();
                        for (int k = 0; k < 3; k++) model.Normals[i * 3 + k] = r.ReadSingle();
                        model.Slots[i] = r.ReadUInt16();
                        model.Shades[i] = r.ReadByte();
                        r.ReadByte();
                    }
                    model.Paint = Indices(r);
                    model.Rest = Indices(r);
                    file.Models.Add(model);
                }
            }
            return file;
        }

        private static int[] Indices(BinaryReader r)
        {
            var list = new int[r.ReadInt32()];
            for (int i = 0; i < list.Length; i++) list[i] = r.ReadUInt16();
            return list;
        }
    }
}
