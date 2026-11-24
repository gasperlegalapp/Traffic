using System;
using System.IO;
using NUnit.Framework;
using Traffic.Art;
using Traffic.Sim;

namespace Traffic.Tests
{
    /// <summary>
    /// Checks the baked art file the game loads. These live here, outside Assets, because
    /// they read a file from the repository, which Unity's own test runner would not find.
    /// </summary>
    public class ArtFileTests
    {
        private static ArtFile Load()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "Assets", "Resources", "Art", "models.bytes"))) dir = Path.GetDirectoryName(dir);
            Assert.That(dir, Is.Not.Null, "models.bytes was not found");
            return ArtFile.Read(File.ReadAllBytes(Path.Combine(dir, "Assets", "Resources", "Art", "models.bytes")));
        }

        [Test]
        public void HoldsVehiclesTreesBushesAndRocks()
        {
            ArtFile file = Load();
            foreach (string kind in new[] { "car/", "truck/", "tree/", "pine/", "bush/", "rock/" })
            {
                Assert.That(file.Models.Exists(m => m.Name.StartsWith(kind)), kind + " models are missing");
            }
        }

        [Test]
        public void VehiclesFitTheSpaceTheSimulationGivesThem()
        {
            foreach (ArtModel m in Load().Models)
            {
                VehicleSpec spec = m.Name.StartsWith("car/") ? VehicleSpec.Of(VehicleKind.Car) : m.Name.StartsWith("truck/") ? VehicleSpec.Of(VehicleKind.Truck) : null;
                if (spec == null) continue;
                // Models keep their own proportions, so they are not exactly the simulation's size.
                // None may be longer, or it would poke into the car ahead; none should be much shorter or wider.
                Assert.That(m.SizeX, Is.LessThanOrEqualTo(spec.Len + 0.01), m.Name + " is longer than the simulation allows");
                Assert.That(m.SizeX, Is.GreaterThan(spec.Len - 0.75), m.Name + " is much shorter than the simulation's vehicle");
                Assert.That(m.SizeZ, Is.EqualTo(spec.Wid).Within(0.4), m.Name + " width");
                // Centered, standing on the ground, with paint to take the car's color.
                double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue;
                for (int i = 0; i < m.Count; i++)
                {
                    minX = Math.Min(minX, m.Positions[i * 3]);
                    maxX = Math.Max(maxX, m.Positions[i * 3]);
                    minY = Math.Min(minY, m.Positions[i * 3 + 1]);
                }
                Assert.That(maxX, Is.EqualTo(-minX).Within(0.01), m.Name + " is not centered");
                Assert.That(minY, Is.EqualTo(0).Within(0.01), m.Name + " is not on the ground");
                Assert.That(m.Paint.Length, Is.GreaterThan(100), m.Name + " has no paint");
            }
        }

        [Test]
        public void EveryTriangleIsSoundAndFacesOutward()
        {
            ArtFile file = Load();
            foreach (ArtModel m in file.Models)
            {
                for (int i = 0; i < m.Count; i++)
                {
                    Assert.That(m.Slots[i] >= ArtFile.Leaf || m.Slots[i] < file.PaletteCount, m.Name + " uses a color that is not in the palette");
                    double nx = m.Normals[i * 3], ny = m.Normals[i * 3 + 1], nz = m.Normals[i * 3 + 2];
                    Assert.That(Math.Sqrt(nx * nx + ny * ny + nz * nz), Is.EqualTo(1).Within(0.01), m.Name + " has a normal that is not unit length");
                }
                foreach (int[] list in new[] { m.Paint, m.Rest })
                {
                    Assert.That(list.Length % 3, Is.EqualTo(0));
                    for (int t = 0; t < list.Length; t += 3)
                    {
                        int a = list[t], b = list[t + 1], c = list[t + 2];
                        Assert.That(a < m.Count && b < m.Count && c < m.Count, m.Name + " has a triangle that points past its corners");
                        // Unity shows the side that cross(b - a, c - a) points out of; that should be the side the normals are on.
                        double ux = m.Positions[b * 3] - m.Positions[a * 3], uy = m.Positions[b * 3 + 1] - m.Positions[a * 3 + 1], uz = m.Positions[b * 3 + 2] - m.Positions[a * 3 + 2];
                        double vx = m.Positions[c * 3] - m.Positions[a * 3], vy = m.Positions[c * 3 + 1] - m.Positions[a * 3 + 1], vz = m.Positions[c * 3 + 2] - m.Positions[a * 3 + 2];
                        double fx = uy * vz - uz * vy, fy = uz * vx - ux * vz, fz = ux * vy - uy * vx;
                        double facing = fx * (m.Normals[a * 3] + m.Normals[b * 3] + m.Normals[c * 3]) + fy * (m.Normals[a * 3 + 1] + m.Normals[b * 3 + 1] + m.Normals[c * 3 + 1]) + fz * (m.Normals[a * 3 + 2] + m.Normals[b * 3 + 2] + m.Normals[c * 3 + 2]);
                        Assert.That(facing, Is.GreaterThanOrEqualTo(-1e-6), m.Name + " has a triangle facing inward");
                    }
                }
            }
        }

        [Test]
        public void CarsPointTheirNoseAlongX()
        {
            // The headlights are the only strong yellow on the sedan, so they show which end is the front.
            ArtFile file = Load();
            ArtModel sedan = file.Models.Find(m => m.Name == "car/sedan");
            double front = 0, back = 0;
            for (int i = 0; i < sedan.Count; i++)
            {
                if (sedan.Slots[i] >= ArtFile.Leaf) continue;
                int r = file.Palette[sedan.Slots[i] * 3], g = file.Palette[sedan.Slots[i] * 3 + 1], b = file.Palette[sedan.Slots[i] * 3 + 2];
                if (r > 200 && g > 150 && b < 120)
                {
                    if (sedan.Positions[i * 3] > 0) front++;
                    else back++;
                }
            }
            Assert.That(front, Is.GreaterThan(0), "no headlights found");
            Assert.That(back, Is.EqualTo(0), "headlights at the back");
        }
    }
}
