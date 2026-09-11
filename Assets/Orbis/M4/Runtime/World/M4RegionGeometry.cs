using System;
using System.Collections.Generic;
using Orbis.M2;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.M4
{
    /// <summary>Original primitive grayboxes. No combat, quests, rewards or scene switching is owned here.</summary>
    public static class M4RegionGeometry
    {
        public static M4RegionLayout Build(Transform parent, M4RegionId id)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            M4RegionCatalog.Get(id); // Reject invalid enum values before creating scene objects.
            if (id == M4RegionId.Agnia) return AgniaLayout();
            var builder = new Builder(parent, id);
            return builder.Build();
        }

        private static M4RegionLayout AgniaLayout() => new M4RegionLayout(
            new Vector3(0, .1f, 0), new[] { new Vector3(11, 0, 8), new Vector3(14, 0, 8), new Vector3(17, 0, 8) },
            new Vector3(14, 0, 10.5f), new[] { new Vector3(11, 0, 26), new Vector3(14, 0, 28), new Vector3(17, 0, 26) },
            new Vector3(14, 0, 21.5f), new Vector3(20, 0, -3), new Vector3(-3, 0, 0), new Vector3(-5, 0, 0));

        private sealed class Builder
        {
            private readonly Transform root;
            private readonly M4RegionId id;
            private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
            private readonly MaterialPropertyBlock tint;
            private readonly Color stone, ground, accent;

            public Builder(Transform parent, M4RegionId id)
            {
                this.id = id;
                root = new GameObject(id + " Region Geometry").transform;
                // Fixed world coordinates are part of the content integration contract.
                root.SetParent(parent, true);
                tint = new MaterialPropertyBlock(); // Build is invoked on Unity's main thread.
                switch (id)
                {
                    case M4RegionId.Teluna:
                        ground = new Color(.66f, .65f, .47f); stone = new Color(.43f, .54f, .56f); accent = new Color32(0x1F, 0xA2, 0xFF, 255); break;
                    case M4RegionId.Zephyr:
                        ground = new Color(.39f, .52f, .32f); stone = new Color(.53f, .54f, .44f); accent = new Color32(0x6C, 0xFF, 0xB8, 255); break;
                    case M4RegionId.Granite:
                        ground = new Color(.40f, .38f, .34f); stone = new Color(.48f, .44f, .37f); accent = new Color32(0xD4, 0xA9, 0x3B, 255); break;
                    default:
                        ground = new Color(.30f, .33f, .39f); stone = new Color(.39f, .40f, .49f); accent = new Color32(0xB2, 0x6C, 0xFF, 255); break;
                }
            }

            public M4RegionLayout Build()
            {
                // Unspecified graybox dimensions default to 70 x 70 m. Each content point has a
                // walkable route; the northern 20 m boss court stays clear of the other activities.
                M4RegionLayout layout;
                switch (id)
                {
                    case M4RegionId.Teluna: layout = Teluna(); break;
                    case M4RegionId.Zephyr: layout = Zephyr(); break;
                    case M4RegionId.Granite: layout = Granite(); break;
                    default: layout = Voltheim(); break;
                }
                BuildPuzzlePlaza(layout);
                BuildChallengeRoom(layout);
                BuildBossArena(layout);
                Mark("Arrival Plaza", layout.Spawn - Vector3.up * .1f, new Vector2(8, 5), accent * .65f);
                Boundaries();
                Physics.SyncTransforms();
                return layout;
            }

            private M4RegionLayout DefaultLayout(float puzzleY = 0, float roomY = 0, float bossY = 0, float puzzleX = -19)
                => new M4RegionLayout(new Vector3(0, .1f, -7),
                    new[] { new Vector3(puzzleX - 4, puzzleY, 9), new Vector3(puzzleX, puzzleY, 9), new Vector3(puzzleX + 4, puzzleY, 9) },
                    new Vector3(puzzleX, puzzleY, 13),
                    new[] { new Vector3(14, roomY, 27), new Vector3(18, roomY, 30), new Vector3(22, roomY, 27) },
                    new Vector3(18, roomY, 22.5f), new Vector3(-8, bossY, 41), new Vector3(-4, 0, -5), new Vector3(-7, 0, -5));

            private M4RegionLayout Teluna()
            {
                Box("Archipelago Seabed", new Vector3(0, -3.25f, 20), new Vector3(70, .5f, 70), "Sand", ground * .65f);
                Box("Arrival Island", new Vector3(0, -1.5f, -6), new Vector3(18, 3, 16), "Sand", ground);
                Box("Coral Cave Island", new Vector3(-22, -1.5f, 11), new Vector3(22, 3, 18), "Sand", ground);
                Box("Central Tide Island", new Vector3(0, -1.5f, 13), new Vector3(12, 3, 12), "Sand", ground);
                Box("Eastern Navigator Island", new Vector3(20, -1.5f, 25), new Vector3(24, 3, 26), "Sand", ground);
                Box("Northern Boss Island", new Vector3(-8, -1.5f, 40), new Vector3(22, 3, 22), "Sand", ground);
                Bridge("Arrival Footbridge", new Vector3(0, 0, 1), new Vector3(0, 0, 9), 3);
                Bridge("Coral Island Footbridge", new Vector3(-4, 0, 12), new Vector3(-14, 0, 12), 3);
                Bridge("Navigator Footbridge", new Vector3(4, 0, 16), new Vector3(12, 0, 18), 3);
                Bridge("Northern Causeway", new Vector3(0, 0, 17), new Vector3(-8, 0, 31), 4);
                // Five-meter shore run / three-meter depth = 31 degree walkable beach.
                Ramp("Swimmable Cove Exit", new Vector3(11, -3, 10), new Vector3(5.8f, 0, 10), 5);
                var water = new GameObject("Teluna Swimming Water");
                water.transform.SetParent(root, false); water.transform.localPosition = new Vector3(0, -1.5f, 20); water.layer = 11;
                water.AddComponent<WaterVolume>().Configure(Vector3.zero, new Vector3(70, 3, 70), 0);
                var surface = Box("Teluna Sea Surface", new Vector3(0, -.025f, 20), new Vector3(70, .025f, 70), "Water", null, false);
                surface.layer = 11; surface.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                surface.GetComponent<Renderer>().receiveShadows = false;
                // An open cave arch leaves a 4 m passage, large enough for the third-person camera.
                Arch("Coral Cave Entrance", new Vector3(-25, 0, 3.5f), 7, 4.2f, 3, stone);
                for (int i = 0; i < 5; i++)
                    Coral(new Vector3(-31 + i * 4.1f, 0, 18.5f), i % 2 == 0 ? new Color(.75f, .43f, .46f) : new Color(.48f, .64f, .60f));
                Coral(new Vector3(30, 0, 14), new Color(.70f, .47f, .56f));
                return DefaultLayout(puzzleX: -23);
            }

            private M4RegionLayout Zephyr()
            {
                FlatLand();
                Mark("Long Grassland Track", new Vector3(0, 0, 16), new Vector2(3.5f, 48), ground * 1.22f);
                Mark("Crosswind Track", new Vector3(0, 0, 8), new Vector2(53, 3), ground * 1.22f);
                Windmill(new Vector3(-31, 0, 26), 12);
                Windmill(new Vector3(26, 0, 6), 16);
                Windmill(new Vector3(26, 0, 45), 13);
                Cliff("Western Gliding Bluff", new Vector3(-27, 3, 43), new Vector3(10, 6, 15));
                Ramp("Grass Bluff Ascent", new Vector3(-27, 0, 25), new Vector3(-27, 6, 35.7f), 5);
                for (int i = 0; i < 6; i++)
                {
                    Box("Nomad Trail Post " + i, new Vector3(5, .8f, -5 + i * 5), new Vector3(.18f, 1.6f, .18f), "Cliff", stone);
                    Box("Crosswind Pennant " + i, new Vector3(5.5f, 1.4f, -5 + i * 5), new Vector3(1, .5f, .04f), "Highlight", accent * .8f, false);
                }
                return DefaultLayout();
            }

            private M4RegionLayout Granite()
            {
                FlatLand();
                Cliff("Western Mining Terrace", new Vector3(-20, 2, 11), new Vector3(24, 4, 20));
                Cliff("Eastern High Mine", new Vector3(20, 4, 27), new Vector3(24, 8, 22));
                Cliff("Northern Summit Mesa", new Vector3(-8, 6, 41), new Vector3(22, 12, 20));
                Ramp("Western Ore Road", new Vector3(-3, 0, 2), new Vector3(-8.2f, 4, 2), 5);
                Box("Switchback Rest Landing", new Vector3(20, 2, 5), new Vector3(6, 4, 4), "Stone", stone);
                Ramp("Lower Mine Switchback", new Vector3(6, 0, -6), new Vector3(18, 4, 3.2f), 5);
                Ramp("Upper Mine Switchback", new Vector3(20, 4, 6), new Vector3(20, 8, 16.2f), 5);
                Box("Summit Rest Landing", new Vector3(-3, 3, 24), new Vector3(8, 6, 6), "Stone", stone);
                Ramp("Lower Summit Road", new Vector3(0, 0, 7), new Vector3(-3, 6, 21.2f), 5);
                Ramp("Upper Summit Road", new Vector3(-3, 6, 23), new Vector3(-8, 12, 31.2f), 5);
                Arch("Ore Tunnel Timber", new Vector3(-21, 4, 18), 7, 4, 3, new Color(.37f, .29f, .20f));
                for (int i = 0; i < 2; i++)
                    Box("Mine Rail " + i, new Vector3(-21 + (i == 0 ? -.5f : .5f), 4.04f, 15.5f), new Vector3(.11f, .08f, 7), "Metal", null, false);
                for (int i = 0; i < 5; i++)
                    Box("Ore Wagon Sleeper " + i, new Vector3(-21, 4.03f, 13 + i), new Vector3(1.5f, .06f, .14f), "Cliff", stone * .7f, false);
                for (int i = 0; i < 5; i++)
                {
                    GameObject ore = Box("Exposed Mineral Seam " + i, new Vector3(30.6f, 8.5f, 19 + i * 3.6f), new Vector3(.9f, 1.5f, .9f), "Highlight", accent, false);
                    ore.transform.localRotation = Quaternion.Euler(0, 45, 20);
                }
                return DefaultLayout(4, 8, 12);
            }

            private M4RegionLayout Voltheim()
            {
                FlatLand();
                Mark("Central Storm Avenue", new Vector3(0, 0, 19), new Vector2(5, 64), accent);
                Mark("Western Service Street", new Vector3(-17, 0, -1), new Vector2(30, 3), stone * 1.18f);
                Mark("Eastern Service Street", new Vector3(17, 0, 17), new Vector2(31, 3), stone * 1.18f);
                StormTower(new Vector3(-30, 0, -4), 12);
                StormTower(new Vector3(-30, 0, 29), 18);
                StormTower(new Vector3(-26, 0, 49), 23);
                StormTower(new Vector3(8, 0, 5), 12);
                StormTower(new Vector3(29, 0, 8), 18);
                StormTower(new Vector3(30, 0, 43), 24);
                StormTower(new Vector3(6.5f, 0, 45), 20);
                StormTower(new Vector3(19, 0, 48), 16);
                // Static silhouettes identify the storm-tower city without adding weather/VFX systems.
                for (int i = 0; i < 3; i++)
                {
                    var cloud = Primitive("Distant Storm Cloud Mass " + i, PrimitiveType.Sphere,
                        new Vector3(-18 + i * 17, 29 + i % 2 * 2, 39 + i % 2 * 5), new Vector3(24, 3, 13), "Stone", new Color(.29f, .31f, .39f), false);
                    cloud.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                }
                for (int i = 0; i < 7; i++)
                {
                    float z = -8 + i * 6;
                    Box("Storm Avenue Lamp Post " + i, new Vector3(3.3f, 1.6f, z), new Vector3(.18f, 3.2f, .18f), "Metal");
                    Box("Lamp Element Cap " + i, new Vector3(3.3f, 3.2f, z), new Vector3(.55f, .2f, .55f), "Highlight", accent, false);
                }
                return DefaultLayout();
            }

            private void StormTower(Vector3 feet, float height)
            {
                Cliff("Climbable Storm Tower", feet + Vector3.up * (height * .5f), new Vector3(5, height, 5));
                Box("Tower Upper Cornice", feet + Vector3.up * (height - 1), new Vector3(5.7f, .35f, 5.7f), "Metal", null, false);
                Primitive("Tower Lightning Spire", PrimitiveType.Cylinder, feet + Vector3.up * (height + 2), new Vector3(.3f, 2, .3f), "Metal", null, false);
                Primitive("Tower Element Beacon", PrimitiveType.Sphere, feet + Vector3.up * (height + 4), Vector3.one * .8f, "Highlight", accent, false);
                for (int i = 0; i < 3; i++)
                    Box("Tower Window Band " + i, feet + new Vector3(0, 3 + i * (height - 5) / 3, -2.515f), new Vector3(3.2f, .3f, .03f), "Highlight", accent * .65f, false);
            }
            private void FlatLand() => Box("Regional Bedrock", new Vector3(0, -1.5f, 20), new Vector3(70, 3, 70), "Ground", ground);

            private void BuildPuzzlePlaza(M4RegionLayout layout)
            {
                Vector3 middle = layout.PuzzleStatuePositions[1];
                Mark("Field Puzzle Plaza", middle + Vector3.forward, new Vector2(13, 9), stone);
                for (int i = 0; i < 3; i++) Mark("Statue Footing " + (i + 1), layout.PuzzleStatuePositions[i], new Vector2(1.7f, 1.7f), accent * .75f);
            }

            private void BuildChallengeRoom(M4RegionLayout layout)
            {
                float y = layout.ChallengeTargets[0].y;
                Mark("Reaction Challenge Floor", new Vector3(18, y, 28), new Vector2(14, 14), stone * .85f);
                Box("Challenge West Wall", new Vector3(11, y + 1.75f, 28), new Vector3(.4f, 3.5f, 14), "Stone", stone);
                Box("Challenge East Wall", new Vector3(25, y + 1.75f, 28), new Vector3(.4f, 3.5f, 14), "Stone", stone);
                Box("Challenge North Wall", new Vector3(18, y + 1.75f, 35), new Vector3(14, 3.5f, .4f), "Stone", stone);
                Box("Challenge Entrance Left", new Vector3(13.25f, y + 1.75f, 21), new Vector3(4.5f, 3.5f, .4f), "Stone", stone);
                Box("Challenge Entrance Right", new Vector3(22.75f, y + 1.75f, 21), new Vector3(4.5f, 3.5f, .4f), "Stone", stone);
                Mark("Challenge Interaction Marker", layout.ChallengeEntry, new Vector2(2, 2), accent);
            }

            private void BuildBossArena(M4RegionLayout layout)
            {
                Vector3 p = layout.BossCenter;
                Mark("Isolated Boss Arena", p, new Vector2(20, 20), stone * .8f);
                Box("Boss Arena West Wall", p + new Vector3(-10, 1, 0), new Vector3(.45f, 2, 20), "Cliff", stone);
                Box("Boss Arena East Wall", p + new Vector3(10, 1, 0), new Vector3(.45f, 2, 20), "Cliff", stone);
                Box("Boss Arena North Wall", p + new Vector3(0, 1, 10), new Vector3(20, 2, .45f), "Cliff", stone);
                Box("Boss Gate Left", p + new Vector3(-6.5f, 1, -10), new Vector3(7, 2, .45f), "Cliff", stone);
                Box("Boss Gate Right", p + new Vector3(6.5f, 1, -10), new Vector3(7, 2, .45f), "Cliff", stone);
                for (int i = 0; i < 4; i++)
                    Mark("Boss Court Inlay " + i, p + new Vector3(i < 2 ? -7.5f : 7.5f, .01f, i % 2 == 0 ? -7.5f : 7.5f), new Vector2(1, 1), accent);
            }

            private void Boundaries()
            {
                Box("Region West Boundary", new Vector3(-35, 0, 20), new Vector3(.6f, 6, 70), "Cliff", stone);
                Box("Region East Boundary", new Vector3(35, 0, 20), new Vector3(.6f, 6, 70), "Cliff", stone);
                Box("Region South Boundary", new Vector3(0, 0, -15), new Vector3(70, 6, .6f), "Cliff", stone);
                Box("Region North Boundary", new Vector3(0, 0, 55), new Vector3(70, 6, .6f), "Cliff", stone);
            }

            private void Mark(string name, Vector3 feet, Vector2 size, Color color)
                => Box(name, feet + Vector3.up * .013f, new Vector3(size.x, .02f, size.y), "Stone", color, false);

            private GameObject Cliff(string name, Vector3 center, Vector3 size)
            {
                var cliff = Box(name, center, size, "Cliff", stone);
                cliff.AddComponent<ClimbableSurface>(); return cliff;
            }

            private void Bridge(string name, Vector3 start, Vector3 end, float width)
            {
                var bridge = Box(name, (start + end) * .5f - Vector3.up * .12f,
                    new Vector3(width, .24f, Vector3.Distance(start, end) + .5f), "Cliff", new Color(.51f, .42f, .29f));
                bridge.transform.localRotation = Quaternion.LookRotation(end - start);
            }

            private void Ramp(string name, Vector3 start, Vector3 end, float width)
            {
                // Small overlapping box ends stay within the controller's step allowance.
                // Every ramp is below the 45 degree ground slope limit; cliffs remain climbable too.
                var ramp = Box(name, (start + end) * .5f - Vector3.up * .17f,
                    new Vector3(width, .35f, Vector3.Distance(start, end) + .55f), "Stone", ground * 1.12f);
                ramp.transform.localRotation = Quaternion.LookRotation(end - start, Vector3.up);
            }

            private void Arch(string name, Vector3 feet, float width, float height, float depth, Color color)
            {
                Box(name + " Left Pillar", feet + new Vector3(-width * .5f + .6f, height * .5f, 0), new Vector3(1.2f, height, depth), "Cliff", color);
                Box(name + " Right Pillar", feet + new Vector3(width * .5f - .6f, height * .5f, 0), new Vector3(1.2f, height, depth), "Cliff", color);
                Box(name + " Roof", feet + Vector3.up * (height + .35f), new Vector3(width + .6f, .7f, depth + .5f), "Cliff", color);
            }

            private void Windmill(Vector3 feet, float height)
            {
                var tower = Primitive("Giant Windmill Tower", PrimitiveType.Cylinder, feet + Vector3.up * (height * .5f), new Vector3(3.2f, height * .5f, 3.2f), "Stone", stone);
                tower.AddComponent<ClimbableSurface>();
                Primitive("Windmill Hub", PrimitiveType.Sphere, feet + new Vector3(0, height - 1, -1.8f), Vector3.one * 1.2f, "Metal", null, false);
                for (int i = 0; i < 4; i++)
                {
                    float angle = 45 + i * 90;
                    Vector3 offset = Quaternion.Euler(0, 0, angle) * new Vector3(0, 3.5f, 0);
                    var blade = Box("Static Windmill Sail " + i, feet + new Vector3(0, height - 1, -2) + offset,
                        new Vector3(1.3f, 6, .16f), "Ground", new Color(.79f, .78f, .62f), false);
                    blade.transform.localRotation = Quaternion.Euler(0, 0, angle);
                }
            }

            private void Coral(Vector3 feet, Color color)
            {
                Primitive("Coral Main Branch", PrimitiveType.Capsule, feet + Vector3.up * .8f, new Vector3(.45f, .8f, .45f), "Stone", color, false);
                for (int i = 0; i < 3; i++)
                {
                    var branch = Primitive("Coral Fork " + i, PrimitiveType.Capsule, feet + new Vector3((i - 1) * .45f, 1.2f, (i % 2) * .3f), new Vector3(.26f, .6f, .26f), "Stone", color, false);
                    branch.transform.localRotation = Quaternion.Euler(0, i * 120, (i - 1) * 35);
                }
            }

            private GameObject Box(string name, Vector3 position, Vector3 size, string material, Color? color = null, bool solid = true)
                => Primitive(name, PrimitiveType.Cube, position, size, material, color, solid);

            private GameObject Primitive(string name, PrimitiveType type, Vector3 position, Vector3 size, string materialName, Color? color, bool solid = true)
            {
                var item = GameObject.CreatePrimitive(type); item.name = name; item.layer = 8;
                item.transform.SetParent(root, false); item.transform.localPosition = position; item.transform.localScale = size;
                var renderer = item.GetComponent<Renderer>();
                if (!materials.TryGetValue(materialName, out var material))
                {
                    material = Resources.Load<Material>("M2/" + materialName);
                    if (material == null) throw new InvalidOperationException("M2 material missing: " + materialName + ". Run the project setup before opening M4.");
                    materials.Add(materialName, material);
                }
                renderer.sharedMaterial = material;
                if (color.HasValue)
                {
                    tint.Clear(); tint.SetColor("_BaseColor", color.Value); tint.SetColor("_Color", color.Value);
                    renderer.SetPropertyBlock(tint);
                }
                if (!solid)
                {
                    var collider = item.GetComponent<Collider>(); collider.enabled = false;
                    if (Application.isPlaying) UnityEngine.Object.Destroy(collider); else UnityEngine.Object.DestroyImmediate(collider);
                }
                return item;
            }
        }
    }
}
