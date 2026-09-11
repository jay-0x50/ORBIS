using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;

namespace Orbis.M3.Editor
{
    /// <summary>Authors real GPU graphs from Unity's licensed context skeleton; never runs in a player.</summary>
    public static class M3VfxBuilder
    {
        public const string Root = "Assets/Orbis/M3/Resources/M3/Effects";
        public static readonly string[] EffectNames = { "Vaporize", "ElectroCharged", "Overload", "Swirl", "Crystallize", "Impact", "Burst", "Residue" };
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private const string TemplatePath = "Packages/com.unity.visualeffectgraph/Editor/Templates/Simple_Burst.vfx";
        private static readonly Dictionary<string, Type> Types = new Dictionary<string, Type>();

        private sealed class Layer
        {
            public string Name, Texture;
            public int Count;
            public float LifeMin, LifeMax, SizeStart, SizeEnd, Gravity, Alpha = 1;
            public Vector3 PositionMin, PositionMax, VelocityMin, VelocityMax;
            public bool Ring, Additive = true;
        }

        public static void Build()
        {
            Directory.CreateDirectory(Root);
            AssetDatabase.Refresh();
            foreach (var shape in new[] { "Soft", "Spark", "Crystal", "Ring", "Star" }) BuildTexture(shape);
            string packageRoot = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(TemplatePath).resolvedPath;
            string template = File.ReadAllText(Path.Combine(packageRoot, "Editor/Templates/Simple_Burst.vfx"));
            foreach (string effect in EffectNames)
            {
                string path = Root + "/" + effect + ".vfx";
                // Existing complete graphs remain editable; Setup does not replace authored values.
                if (HasContract(AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(path))) continue;
                Layer[] layers = Layers(effect);
                File.WriteAllText(path, DuplicateSystems(template, effect, layers.Length));
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var asset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(path);
                if (asset == null) throw new InvalidOperationException("Could not import GPU graph: " + path);
                object resource = Static("UnityEditor.VFX.VisualEffectObjectExtensions", "GetResource", asset);
                object graph = Static("UnityEditor.VFX.VisualEffectResourceExtensions", "GetGraph", resource);
                object primary = Parameter(graph, "PrimaryColor", typeof(Vector4), new Vector4(1f, .45f, .16f, 1));
                object secondary = Parameter(graph, "SecondaryColor", typeof(Vector4), new Vector4(1f, .86f, .55f, 1));
                object scale = Parameter(graph, "Scale", typeof(float), 1f);
                object[] contexts = Items(Get(graph, "children")).Where(x => x.GetType().Name.Contains("Spawner") || x.GetType().Name.Contains("Initialize") || x.GetType().Name.Contains("Update") || x.GetType().Name.Contains("Output")).ToArray();
                if (contexts.Length != layers.Length * 4) throw new InvalidOperationException(effect + ": invalid system skeleton.");
                for (int i = 0; i < layers.Length; i++) AuthorLayer(contexts.Skip(i * 4).Take(4).ToArray(), layers[i], primary, secondary, scale, i);
                var updateProperty = Property(resource.GetType(), "updateMode");
                updateProperty.SetValue(resource, Enum.Parse(updateProperty.PropertyType, "DeltaTime, IgnoreTimeScale"));
                Static("UnityEditor.VFX.VisualEffectResourceExtensions", "WriteAssetWithSubAssets", resource);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                Debug.Log("[M3] Authored GPU effect " + effect + " (" + layers.Length + " one-shot systems; last particle <= " + layers.Max(x => x.LifeMax).ToString("0.0", CultureInfo.InvariantCulture) + " s).");
            }
            AssetDatabase.SaveAssets();
            Validate();
        }

        public static void Validate()
        {
            foreach (string effect in EffectNames)
            {
                var asset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(Root + "/" + effect + ".vfx");
                if (!HasContract(asset)) throw new InvalidOperationException(effect + ": expected GPU asset with PrimaryColor/SecondaryColor Vector4 and Scale float.");
                object resource = Static("UnityEditor.VFX.VisualEffectObjectExtensions", "GetResource", asset);
                object graph = Static("UnityEditor.VFX.VisualEffectResourceExtensions", "GetGraph", resource);
                foreach (object spawn in Items(Get(graph, "children")).Where(x => x.GetType().Name.Contains("Spawner")))
                {
                    if (Get(spawn, "loopCount").ToString() != "Constant" || Convert.ToInt32(Get(FindInput(spawn, "LoopCount"), "value")) != 1)
                        throw new InvalidOperationException(effect + ": GPU emitter must be a single burst.");
                }
                if (!Get(resource, "updateMode").ToString().Contains("IgnoreTimeScale")) throw new InvalidOperationException(effect + ": GPU effect must ignore gameplay hitstop.");
                if (Convert.ToInt32(Call(resource, "GetShaderSourceCount")) == 0) throw new InvalidOperationException(effect + ": GPU graph produced no shader source.");
            }
            Debug.Log("[M3] Eight VFX Graph assets validated: finite GPU emitters and pooled runtime property contracts.");
        }

        private static bool HasContract(VisualEffectAsset asset)
        {
            if (asset == null) return false;
            var properties = new List<VFXExposedProperty>();
            asset.GetExposedProperties(properties);
            return properties.Any(x => x.name == "PrimaryColor" && x.type == typeof(Vector4))
                && properties.Any(x => x.name == "SecondaryColor" && x.type == typeof(Vector4))
                && properties.Any(x => x.name == "Scale" && x.type == typeof(float));
        }

        private static Layer[] Layers(string effect)
        {
            // Spec03 defines appearance, not GPU budgets or particle curves. These bounded defaults
            // reserve <= 256 particles per layer and <= 2.3 s life (residue: requested 4 s).
            Layer L(string name, string texture, int count, float minLife, float maxLife, float size0, float size1, Vector3 p, Vector3 v0, Vector3 v1, float gravity = 0)
                => new Layer { Name = name, Texture = texture, Count = count, LifeMin = minLife, LifeMax = maxLife, SizeStart = size0, SizeEnd = size1, PositionMin = -p, PositionMax = p, VelocityMin = v0, VelocityMax = v1, Gravity = gravity };
            Layer Ring(float life, float radius) => new Layer { Name = "Expanding shock ring", Texture = "Ring", Count = 1, LifeMin = life, LifeMax = life, SizeStart = .15f, SizeEnd = radius * 2, Ring = true, PositionMin = new Vector3(0, .035f, 0), PositionMax = new Vector3(0, .035f, 0) };
            switch (effect)
            {
                case "Vaporize":
                    var steam = L("Rising steam plumes", "Soft", 44, 1.35f, 2.1f, .30f, 1.30f, new Vector3(.35f, .1f, .35f), new Vector3(-.28f, .8f, -.28f), new Vector3(.28f, 1.8f, .28f));
                    steam.Additive = false; steam.Alpha = .36f;
                    return new[] { steam, L("Hot upward droplets", "Spark", 30, .5f, 1.1f, .16f, .04f, new Vector3(.25f, .1f, .25f), new Vector3(-.7f, 1f, -.7f), new Vector3(.7f, 3f, .7f), -.6f) };
                case "ElectroCharged": return new[] { L("Crackling electric needles", "Spark", 84, .18f, .48f, .22f, .015f, Vector3.one * .14f, Vector3.one * -6f, Vector3.one * 6f, -1.5f), L("Electric star nodes", "Star", 18, .2f, .65f, .3f, .02f, Vector3.one * .6f, Vector3.one * -.15f, Vector3.one * .15f) };
                case "Overload": return new[] { Ring(.75f, 3f), L("Explosive radial sparks", "Spark", 140, .35f, 1.05f, .27f, .025f, Vector3.one * .12f, new Vector3(-7f, .4f, -7f), new Vector3(7f, 5f, 7f), -5f) };
                case "Swirl": return new[] { Ring(1.15f, 2.8f), L("Element tinted outward motes", "Soft", 90, .8f, 1.65f, .15f, .055f, new Vector3(.35f, .08f, .35f), new Vector3(-2.5f, .12f, -2.5f), new Vector3(2.5f, .9f, 2.5f), .25f) };
                case "Crystallize": return new[] { L("Faceted crystal scattering", "Crystal", 46, .8f, 1.65f, .23f, .065f, new Vector3(.22f, .1f, .22f), new Vector3(-2.6f, 1f, -2.6f), new Vector3(2.6f, 4f, 2.6f), -5.5f) };
                case "Impact": return new[] { L("Element impact sparks", "Star", 32, .18f, .5f, .22f, .015f, Vector3.one * .06f, Vector3.one * -3f, Vector3.one * 3f, -1f) };
                case "Burst": return new[] { Ring(1.3f, 5f), L("Ultimate constellation eruption", "Star", 240, 1.2f, 2.3f, .32f, .045f, new Vector3(.55f, .25f, .55f), new Vector3(-3.2f, .65f, -3.2f), new Vector3(3.2f, 4.2f, 3.2f), -.7f) };
                case "Residue": return new[] { L("Four second elemental residue", "Soft", 76, 3.6f, 4f, .085f, .03f, new Vector3(1.7f, .15f, 1.7f), new Vector3(-.12f, .13f, -.12f), new Vector3(.12f, .48f, .12f)) };
                default: throw new ArgumentOutOfRangeException(nameof(effect));
            }
        }

        private static void AuthorLayer(object[] c, Layer layer, object primary, object secondary, object scale, int index)
        {
            object spawn = c.Single(x => x.GetType().Name.Contains("Spawner"));
            object init = c.Single(x => x.GetType().Name.Contains("Initialize"));
            object update = c.Single(x => x.GetType().Name.Contains("Update"));
            object output = c.Single(x => x.GetType().Name.Contains("Output"));
            for (int j = 0; j < c.Length; j++) Set(c[j], "m_UIPosition", new Vector2(300 + index * 610, 300 + j * 430));
            Set(spawn, "m_Label", layer.Name);
            Setting(spawn, "loopCount", "Constant");
            Setting(spawn, "loopDuration", "Constant");
            SetSlot(FindInput(spawn, "LoopDuration"), .05f);
            SetSlot(FindInput(spawn, "LoopCount"), 1);
            object burst = Items(Get(spawn, "children")).Single();
            SetSlot(Inputs(burst)[0], (float)layer.Count);
            SetSlot(Inputs(burst)[1], 0f);
            object data = Call(init, "GetData");
            Set(data, "space", Enum.Parse(Property(data.GetType(), "space").PropertyType, "Local"));
            Setting(data, "capacity", (uint)Mathf.NextPowerOfTwo(Mathf.Max(32, layer.Count)));
            Set(data, "title", layer.Name);
            ClearBlocks(init);
            Attribute(init, "lifetime", layer.LifeMin, layer.LifeMax);
            Attribute(init, "position", layer.PositionMin, layer.PositionMax);
            Attribute(init, "velocity", layer.VelocityMin, layer.VelocityMax);
            Attribute(init, "angle", new Vector3(0, 0, -180), new Vector3(0, 0, 180));
            object tint = Attribute(init, "color", Vector3.one, Vector3.one, "Uniform");
            Link(primary, Inputs(tint)[0]); Link(secondary, Inputs(tint)[1]);
            // Scale controls geometry AND travel; runtime therefore only needs SetFloat("Scale", x).
            foreach (string attribute in new[] { "position", "velocity" })
            {
                object mul = Attribute(init, attribute, Vector3.one, null);
                Setting(mul, "Composition", "Multiply"); Link(scale, Inputs(mul)[0]);
            }
            foreach (object b in Items(Get(update, "children")))
            {
                if (b.GetType().Name == "Gravity") SetSlot(Inputs(b)[0], new Vector3(0, layer.Gravity, 0));
                else if (b.GetType().Name.Contains("Drag")) SetSlot(Inputs(b)[0], layer.Ring ? 0f : .35f);
            }
            object orient = Items(Get(output, "children")).Single(x => x.GetType().Name == "Orient");
            object sizeCurve = Items(Get(output, "children")).Single(x => x.GetType().Name == "AttributeFromCurve" && (string)Get(x, "attribute") == "size");
            object colorCurve = Items(Get(output, "children")).Single(x => x.GetType().Name == "AttributeFromCurve" && (string)Get(x, "attribute") == "color");
            if (layer.Ring)
            {
                Setting(orient, "mode", "Advanced"); Setting(orient, "axes", "XY");
                SetSlot(Inputs(orient)[0], Vector3.right); SetSlot(Inputs(orient)[1], Vector3.forward);
                // Keep the ring anchored to the horizontal plane: no random Euler rotation.
                object angle = Items(Get(init, "children")).Single(x => x.GetType().Name == "SetAttribute" && (string)Get(x, "attribute") == "angle");
                SetSlot(Inputs(angle)[0], Vector3.zero); SetSlot(Inputs(angle)[1], Vector3.zero);
            }
            Setting(sizeCurve, "Composition", "Overwrite");
            SetSlot(Inputs(sizeCurve)[0], AnimationCurve.Linear(0, layer.SizeStart, 1, layer.SizeEnd));
            Setting(colorCurve, "Composition", "Multiply");
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(layer.Alpha, .045f), new GradientAlphaKey(layer.Alpha * .7f, .55f), new GradientAlphaKey(0, 1) });
            SetSlot(Inputs(colorCurve)[0], fade);
            object sizeScale = Attribute(output, "size", 1f, null);
            Setting(sizeScale, "Composition", "Multiply"); Link(scale, Inputs(sizeScale)[0]);
            Setting(output, "blendMode", layer.Additive ? "Additive" : "Alpha");
            SetSlot(Inputs(output)[0], AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/" + layer.Texture + ".png"));
            // Template bounds are made generous enough for all authored finite bursts and Scale <= 2.
            object bounds = Inputs(init).FirstOrDefault(x => ((string)Get(Get(x, "property"), "name")).Equals("bounds", StringComparison.OrdinalIgnoreCase));
            if (bounds != null)
            {
                object value = Get(bounds, "value");
                Set(value, "center", new Vector3(0, 2, 0)); Set(value, "size", new Vector3(40, 32, 40)); SetSlot(bounds, value);
            }
        }

        private static object Parameter(object graph, string name, Type type, object value)
        {
            object p = ScriptableObject.CreateInstance(TypeOf("UnityEditor.VFX.VFXParameter"));
            Call(p, "Init", type); Call(graph, "AddChild", p, -1, true);
            Setting(p, "m_ExposedName", name); Setting(p, "m_Exposed", true); Set(p, "value", value);
            Set(p, "m_UIPosition", new Vector2(-180, name == "PrimaryColor" ? 380 : name == "SecondaryColor" ? 530 : 680));
            return Items(Get(p, "outputSlots")).Single();
        }

        private static object Attribute(object context, string name, object min, object max, string random = "PerComponent")
        {
            object b = ScriptableObject.CreateInstance(TypeOf("UnityEditor.VFX.Block.SetAttribute"));
            Call(context, "AddChild", b, -1, true);
            Setting(b, "attribute", name); Setting(b, "Random", max == null ? "Off" : random);
            object[] slots = Inputs(b); SetSlot(slots[0], min); if (max != null) SetSlot(slots[1], max);
            return b;
        }

        private static void ClearBlocks(object context)
        {
            foreach (object child in Items(Get(context, "children")).ToArray()) Call(context, "RemoveChild", child, true);
        }

        private static void Link(object source, object input)
        {
            if (!(bool)Call(input, "Link", source, true)) throw new InvalidOperationException("Cannot connect exposed GPU parameter to " + Get(Get(input, "property"), "name"));
        }

        private static object[] Inputs(object model) => Items(Get(model, "inputSlots")).ToArray();
        private static object FindInput(object model, string name) => Inputs(model).Single(x => (string)Get(Get(x, "property"), "name") == name);
        private static IEnumerable<object> Items(object list) => ((IEnumerable)list).Cast<object>();

        private static void SetSlot(object slot, object value)
        {
            object old = Get(slot, "value");
            // VFX Position/Direction/Vector wrapper types store a Vector3 field rather than Vector3 itself.
            if (value is Vector3 && old != null && old.GetType() != typeof(Vector3))
            {
                var member = old.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault(x => x.FieldType == typeof(Vector3));
                if (member != null) { member.SetValue(old, value); value = old; }
            }
            Set(slot, "value", value);
        }

        private static void Setting(object obj, string name, object value)
        {
            var field = Field(obj.GetType(), name);
            if (field.FieldType.IsEnum && value is string s) value = Enum.Parse(field.FieldType, s);
            Call(obj, "SetSettingValue", name, value);
        }

        private static Type TypeOf(string name)
        {
            if (!Types.TryGetValue(name, out Type result))
                Types[name] = result = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType(name)).FirstOrDefault(x => x != null) ?? throw new TypeLoadException(name);
            return result;
        }
        private static FieldInfo Field(Type type, string name)
        {
            for (; type != null; type = type.BaseType) { var f = type.GetField(name, Flags); if (f != null) return f; }
            throw new MissingFieldException(name);
        }
        private static PropertyInfo Property(Type type, string name)
        {
            for (; type != null; type = type.BaseType) { var p = type.GetProperty(name, Flags); if (p != null) return p; }
            return null;
        }
        private static object Get(object obj, string name)
        {
            var p = Property(obj.GetType(), name); return p != null ? p.GetValue(obj) : Field(obj.GetType(), name).GetValue(obj);
        }
        private static void Set(object obj, string name, object value)
        {
            var p = Property(obj.GetType(), name);
            if (p != null && p.CanWrite) p.SetValue(obj, value); else Field(obj.GetType(), name).SetValue(obj, value);
        }
        private static object Call(object obj, string name, params object[] args)
        {
            for (Type t = obj.GetType(); t != null; t = t.BaseType)
                foreach (var method in t.GetMethods(Flags).Where(x => x.Name == name && x.GetParameters().Length == args.Length))
                    if (method.GetParameters().Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i])).All(x => x))
                        return method.Invoke(obj, args);
            throw new MissingMethodException(obj.GetType().FullName, name + "/" + args.Length);
        }
        private static object Static(string type, string name, params object[] args) => TypeOf(type).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Single(x => x.Name == name && x.GetParameters().Length == args.Length).Invoke(null, args);

        private static string DuplicateSystems(string yaml, string name, int count)
        {
            const string graphId = "114350483966674976";
            var documents = Regex.Matches(yaml, @"(?ms)^--- !u!.*?(?=^--- !u!|\z)").Cast<Match>().Select(x => x.Value).ToArray();
            string Id(string doc) => Regex.Match(doc, @"^--- !u!\d+ &(\d+)").Groups[1].Value;
            var globals = new HashSet<string> { graphId, "114340500867371532", "8926484042661614527" };
            string root = documents.Single(d => Id(d) == graphId);
            string[] contextIds = { "8926484042661614700", "8926484042661614707", "8926484042661614756", "8926484042661614775" };
            string extraChildren = "";
            var clones = new List<string>();
            for (int n = 1; n < count; n++)
            {
                var map = documents.Where(d => !globals.Contains(Id(d))).ToDictionary(Id, d => (long.Parse(Id(d), CultureInfo.InvariantCulture) + n * 100000L).ToString(CultureInfo.InvariantCulture));
                foreach (string id in contextIds) extraChildren += "  - {fileID: " + map[id] + "}\n";
                foreach (string doc in documents.Where(d => !globals.Contains(Id(d))))
                    clones.Add(Regex.Replace(doc, @"(?<=&|fileID: )\d+", m => map.TryGetValue(m.Value, out var replacement) ? replacement : m.Value));
            }
            root = root.Replace("  m_UIPosition:", extraChildren + "  m_UIPosition:").Replace("m_Name: 03_Simple_Burst", "m_Name: " + name);
            return "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n" + string.Concat(documents.Select(d => Id(d) == graphId ? root : d.Replace("m_Name: Simple_Burst", "m_Name: " + name))) + string.Concat(clones);
        }

        private static void BuildTexture(string shape)
        {
            string path = Root + "/" + shape + ".png";
            if (File.Exists(path)) return;
            const int n = 128;
            var texture = new Texture2D(n, n, TextureFormat.RGBA32, false, true);
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float u = (x + .5f) / n * 2 - 1, v = (y + .5f) / n * 2 - 1;
                float r = Mathf.Sqrt(u * u + v * v), alpha, shade = 1;
                switch (shape)
                {
                    case "Spark": alpha = Mathf.Pow(Mathf.Clamp01(1 - Mathf.Abs(u) / .16f), 1.5f) * Mathf.Pow(Mathf.Clamp01(1 - Mathf.Abs(v)), .65f); break;
                    case "Ring": alpha = Mathf.Exp(-Mathf.Pow((r - .76f) * 35, 2)); break;
                    case "Crystal": alpha = Mathf.Clamp01((1 - Mathf.Abs(u) * 1.5f - Mathf.Abs(v)) * 30); shade = u < 0 ? .62f : 1f; break;
                    case "Star": alpha = Mathf.Clamp01(Mathf.Exp(-r * r * 15) + Mathf.Pow(Mathf.Clamp01(1 - Mathf.Abs(u) * 18), 2) * Mathf.Clamp01(1 - Mathf.Abs(v)) + Mathf.Pow(Mathf.Clamp01(1 - Mathf.Abs(v) * 18), 2) * Mathf.Clamp01(1 - Mathf.Abs(u))); break;
                    default: alpha = Mathf.Pow(Mathf.Clamp01(1 - r * r), 2.5f); break;
                }
                texture.SetPixel(x, y, new Color(shade, shade, shade, alpha));
            }
            texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaSource = TextureImporterAlphaSource.FromInput; importer.alphaIsTransparency = true;
            importer.sRGBTexture = false; importer.mipmapEnabled = true; importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
        }
    }
}
