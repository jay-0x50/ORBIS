"""Regenerate Orbis' editable URP 17 ExplorerToon Shader Graph (Python stdlib only).

Run: python Tools/LookDev/generate_explorer_shader.py
The HLSL file is hand-authored alongside the graph; this script never overwrites it.
Graph, node, property and include GUIDs use a fixed namespace for stable regeneration.
Schema is based on the installed Shader Graph/URP 17 source and Orbis' M3 generator.
"""
from pathlib import Path
import hashlib
import json
import re
import uuid

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Assets/Orbis/Game/LookDev/Shaders"
NAME = "ExplorerToon"
NS = uuid.UUID("6e9d1036-d649-4dc5-9404-a86a13ac5096")
SG_IMPORTER = "625f186215c104763be7675aa2d941aa"


def uid(key):
    return uuid.uuid5(NS, key).hex


def ref(identifier):
    return {"m_Id": identifier}


def vector(n, values=None):
    return dict(zip("xyzw"[:n], values if values is not None else [0.0] * n))


def rgba(values):
    return dict(zip("rgba", values))


def write_meta(path, importer):
    guid = uid("asset/" + path.name)
    meta = Path(str(path) + ".meta")
    if meta.exists():
        existing = re.search(r"^guid:\s*([a-fA-F0-9]{32})", meta.read_text(encoding="utf-8"), re.MULTILINE)
        if not existing or existing.group(1).lower() != guid:
            raise RuntimeError(f"Refusing to replace an unrelated asset GUID: {meta}")
        return guid
    if importer == "graph":
        body = (
            "ScriptedImporter:\n"
            "  internalIDToNameTable: []\n"
            "  externalObjects: {}\n"
            "  serializedVersion: 2\n"
            "  userData: \n"
            "  assetBundleName: \n"
            "  assetBundleVariant: \n"
            f"  script: {{fileID: 11500000, guid: {SG_IMPORTER}, type: 3}}\n"
            "  useAsTemplate: 0\n"
            "  exposeTemplateAsShader: 0\n"
            "  indexedData: {instanceID: 0}\n"
            "  template:\n"
            "    name: \n"
            "    category: \n"
            "    description: \n"
            "    icon: {instanceID: 0}\n"
            "    thumbnail: {instanceID: 0}\n"
            "    order: 0\n"
        )
    else:
        body = (
            "ShaderImporter:\n"
            "  externalObjects: {}\n"
            "  defaultTextures: []\n"
            "  nonModifiableTextures: []\n"
            "  userData: \n"
            "  assetBundleName: \n"
            "  assetBundleVariant: \n"
        )
    meta.write_text(f"fileFormatVersion: 2\nguid: {guid}\n" + body, encoding="utf-8", newline="\n")
    return guid


class Graph:
    def __init__(self, include_guid):
        self.include_guid = include_guid
        self.objects, self.nodes, self.properties, self.edges = [], [], [], []
        self.vertex_blocks, self.fragment_blocks = [], []
        self.categories = {"Surface and ramp": [], "Reference palette": [], "Face lighting": [], "Edge and highlights": [], "Outline and M3": []}

    def obj(self, key, kind, **fields):
        item = dict(m_SGVersion=0, m_Type="UnityEditor.ShaderGraph." + kind, m_ObjectId=uid("object/" + key))
        item.update(fields)
        self.objects.append(item)
        return item

    def slot(self, key, index, name, width=1, output=False, stage=3, kind=None, **fields):
        slot = self.obj(key, kind or f"Vector{width}MaterialSlot", m_Id=index, m_DisplayName=name,
                        m_SlotType=int(output), m_Hidden=False, m_ShaderOutputName=name,
                        m_StageCapability=stage)
        if kind in ("Texture2DMaterialSlot", "Texture2DInputMaterialSlot"):
            slot["m_BareResource"] = False
            if not output:
                slot.update(m_Texture={"m_SerializedTexture": "", "m_Guid": ""}, m_DefaultType=0)
        else:
            value = 0.0 if width == 1 else vector(width)
            slot.update(m_Value=value, m_DefaultValue=value, m_Labels=[])
        slot.update(fields)
        return slot

    def node(self, key, kind, name, slots, x=0, y=0, **fields):
        item = self.obj(key, kind, m_Group=ref(""), m_Name=name,
                        m_DrawState={"m_Expanded": True, "m_Position": {
                            "serializedVersion": "2", "x": x, "y": y, "width": 250, "height": 140}},
                        m_Slots=[ref(s["m_ObjectId"]) for s in slots], synonyms=[],
                        m_Precision=1, m_PreviewExpanded=False, m_DismissedVersion=0,
                        m_PreviewMode=0, m_CustomColors={"m_SerializableColors": []}, **fields)
        self.nodes.append(ref(item["m_ObjectId"]))
        return item

    def connect(self, source, source_slot, destination, destination_slot):
        self.edges.append({
            "m_OutputSlot": {"m_Node": ref(source["m_ObjectId"]), "m_SlotId": source_slot},
            "m_InputSlot": {"m_Node": ref(destination["m_ObjectId"]), "m_SlotId": destination_slot},
        })

    def property(self, reference, display, value, kind="float", category="Surface and ramp",
                 y=0, hidden=False, main=False, hdr=False, maximum=1.0):
        key = reference.lstrip("_")
        property_type = {"float": "Vector1ShaderProperty", "color": "ColorShaderProperty", "texture": "Texture2DShaderProperty",
                         "vector3": "Vector3ShaderProperty"}[kind]
        prop = self.obj("property/" + key, "Internal." + property_type,
                        m_Guid={"m_GuidSerialized": str(uuid.UUID(uid("property-guid/" + key)))},
                        m_Name=display, m_DefaultRefNameVersion=1, m_RefNameGeneratedByDisplayName=display,
                        m_DefaultReferenceName=reference, m_OverrideReferenceName=reference,
                        m_GeneratePropertyBlock=True, m_UseCustomSlotLabel=False, m_CustomSlotLabel="",
                        m_DismissedVersion=0, m_Precision=1, overrideHLSLDeclaration=False,
                        hlslDeclarationOverride=0, m_Hidden=hidden, m_PerRendererData=False,
                        m_customAttributes=[])
        if kind == "float":
            prop.update(m_SGVersion=1, m_Value=value, m_FloatType=1,
                        m_RangeValues={"x": 0.0, "y": maximum}, m_LiteralFloatMode=False)
            slot = self.slot("property-slot/" + key, 0, "Out", output=True)
        elif kind == "color":
            prop.update(m_SGVersion=3, m_Value=rgba(value), isMainColor=main, m_ColorMode=int(hdr))
            slot = self.slot("property-slot/" + key, 0, "Out", 4, output=True)
        elif kind == "vector3":
            # URP 17 VectorShaderProperty serializes a Vector4 value, even for float3 declarations.
            prop.update(m_SGVersion=1, m_Value=vector(4, (*value, 0.0)))
            slot = self.slot("property-slot/" + key, 0, "Out", 3, output=True)
        else:
            prop.update(m_Value={"m_SerializedTexture": "", "m_Guid": ""}, isMainTexture=main,
                        useTilingAndOffset=main, useTexelSize=True, isHDR=False, m_Modifiable=True, m_DefaultType=0)
            slot = self.slot("property-slot/" + key, 0, "Out", output=True, kind="Texture2DMaterialSlot")
        self.properties.append(ref(prop["m_ObjectId"]))
        self.categories[category].append(ref(prop["m_ObjectId"]))
        return self.node("property-node/" + key, "PropertyNode", display, [slot], -1450, y,
                         m_Property=ref(prop["m_ObjectId"]))

    def geometry(self, key, kind, width, space=None, x=-900, y=0):
        fields = {}
        if space is not None:
            fields["m_Space"] = space
        if kind == "PositionNode":
            fields["m_PositionSource"] = 0
        if kind == "UVNode":
            fields["m_OutputChannel"] = 0
        item = self.node("geometry/" + key, kind, key, [self.slot("geometry-slot/" + key, 0, "Out", width, output=True)],
                         x, y, **fields)
        if kind == "PositionNode":
            item["m_SGVersion"] = 1
        return item

    def block(self, name, width, vertex=False, kind=None, default=None, **fields):
        stage = 1 if vertex else 2
        slot = self.slot("block-slot/" + name, 0, name, width, stage=stage, kind=kind, **fields)
        if default is not None:
            slot["m_Value"] = slot["m_DefaultValue"] = default
        descriptor = ("VertexDescription." if vertex else "SurfaceDescription.") + name
        item = self.node("block/" + name, "BlockNode", descriptor, [slot], 1800, -700 if vertex else 200,
                         m_SerializedDescriptor=descriptor)
        (self.vertex_blocks if vertex else self.fragment_blocks).append(ref(item["m_ObjectId"]))
        return item

    def custom_interpolator(self, name, source):
        # Explicit pre-hull world position avoids a different dissolve noise on the expanded outline.
        slot = self.slot("custom-block-slot/" + name, 0, name, 3, stage=1)
        block = self.node("custom-block/" + name, "BlockNode", "VertexDescription.CustomInterpolator", [slot],
                          1800, -500, m_SerializedDescriptor="VertexDescription." + name + "#3#")
        self.vertex_blocks.append(ref(block["m_ObjectId"]))
        self.connect(source, 0, block, 0)
        output = self.slot("custom-node-slot/" + name, 0, "Out", 3, output=True, stage=2)
        return self.node("custom-node/" + name, "CustomInterpolatorNode", name + " (Custom Interpolator)",
                         [output], 100, 850, customBlockNodeName=name, serializedType=3)

    def function(self, name, inputs, outputs, x, y, stage=3):
        # Each input is (name, width or 'texture', source node, source slot).
        slots = []
        for index, (label, width, _, _) in enumerate(inputs):
            slots.append(self.slot(name + "/input/" + label, index, label, width if width != "texture" else 1,
                                   stage=stage, kind="Texture2DInputMaterialSlot" if width == "texture" else None))
        for index, (label, width) in enumerate(outputs, len(inputs)):
            slots.append(self.slot(name + "/output/" + label, index, label, width, output=True, stage=stage))
        node = self.node("function/" + name, "CustomFunctionNode", name + " (Custom Function)", slots, x, y,
                         m_SourceType=0, m_FunctionName=name, m_FunctionSource=self.include_guid,
                         m_FunctionSourceUsePragmas=False, m_FunctionBody="")
        node["m_SGVersion"] = 1
        for index, (_, _, source, source_slot) in enumerate(inputs):
            self.connect(source, source_slot, node, index)
        return node

    def save(self):
        categories = []
        for name, properties in self.categories.items():
            category = self.obj("category/" + name, "CategoryData", m_Name=name, m_ChildObjectList=properties)
            categories.append(ref(category["m_ObjectId"]))
        target = self.obj("target", "Unused", m_Datas=[], m_ActiveSubTarget=ref(uid("object/unlit")))
        target.update(m_SGVersion=1, m_Type="UnityEditor.Rendering.Universal.ShaderGraph.UniversalTarget",
                      m_AllowMaterialOverride=True, m_SurfaceType=0, m_ZTestMode=4, m_ZWriteControl=0,
                      m_AlphaMode=0, m_RenderFace=2, m_AlphaClip=True, m_CastShadows=True, m_ReceiveShadows=True,
                      m_DisableTint=False, m_AdditionalMotionVectorMode=0, m_AlembicMotionVectors=False,
                      m_SupportsLODCrossFade=False, m_CustomEditorGUI="", m_SupportVFX=False)
        unlit = self.obj("unlit", "Unused")
        unlit.update(m_SGVersion=2, m_Type="UnityEditor.Rendering.Universal.ShaderGraph.UniversalUnlitSubTarget",
                     m_KeepLightingVariants=True, m_DefaultDecalBlending=False, m_DefaultSSAO=False)
        graph = dict(m_SGVersion=3, m_Type="UnityEditor.ShaderGraph.GraphData", m_ObjectId=uid("graph"),
                     m_Properties=self.properties, m_Keywords=[], m_Dropdowns=[], m_CategoryData=categories,
                     m_Nodes=self.nodes, m_GroupDatas=[], m_StickyNoteDatas=[], m_Edges=self.edges,
                     m_VertexContext={"m_Position": {"x": 1800, "y": -700}, "m_Blocks": self.vertex_blocks},
                     m_FragmentContext={"m_Position": {"x": 1800, "y": 200}, "m_Blocks": self.fragment_blocks},
                     m_PreviewData={"serializedMesh": {"m_SerializedMesh": '{"mesh":{"instanceID":0}}', "m_Guid": ""},
                                    "preventRotation": False},
                     m_Path="Orbis/LookDev", m_GraphPrecision=0, m_PreviewMode=2, m_OutputNode=ref(""),
                     m_SubDatas=[], m_ActiveTargets=[ref(target["m_ObjectId"])])
        data = [graph] + self.objects
        validate(data)
        path = OUT / (NAME + ".shadergraph")
        path.write_text("\n\n".join(json.dumps(item, indent=4) for item in data) + "\n", encoding="utf-8", newline="\n")
        write_meta(path, "graph")
        return path


def validate(data):
    identifiers = [item["m_ObjectId"] for item in data]
    if len(identifiers) != len(set(identifiers)):
        raise RuntimeError("Duplicate Shader Graph object ID")
    lookup = {item["m_ObjectId"]: item for item in data}
    for edge in data[0]["m_Edges"]:
        for direction in ("m_OutputSlot", "m_InputSlot"):
            connection = edge[direction]
            node = lookup[connection["m_Node"]["m_Id"]]
            slots = [lookup[slot["m_Id"]]["m_Id"] for slot in node["m_Slots"]]
            if connection["m_SlotId"] not in slots:
                raise RuntimeError("Dangling Shader Graph slot connection")
    props = [lookup[item["m_Id"]]["m_OverrideReferenceName"] for item in data[0]["m_Properties"]]
    assert len(props) == len(set(props))
    assert not {"_Cull", "_ZWrite", "_ZTest", "_SrcBlend", "_DstBlend"}.intersection(props), \
        "UniversalTarget owns material override render-state properties."
    assert {"_BaseMap", "_Ramp", "_OutlineOnly", "_Threshold", "_OrbisExplorerToon"}.issubset(props)


def build():
    OUT.mkdir(parents=True, exist_ok=True)
    include = OUT / "ExplorerToonLighting.hlsl"
    if not include.exists():
        raise FileNotFoundError(include)
    graph = Graph(write_meta(include, "include"))
    p = {}
    definitions = [
        ("_BaseMap", "Base Map", None, "texture", "Surface and ramp", False, True, False, 1),
        ("_BaseColor", "Base Color", (1, 1, 1, 1), "color", "Surface and ramp", False, True, False, 1),
        ("_Ramp", "Three Band Ramp", None, "texture", "Surface and ramp", False, False, False, 1),
        ("_ShadowColor", "Shadow Color", (.48, .52, .63, 1), "color", "Surface and ramp", False, False, False, 1),
        ("_EmissionColor", "Emission Color", (0, 0, 0, 1), "color", "Surface and ramp", False, False, True, 1),
        ("_PaletteColor", "Reference Palette", (1, 1, 1, 1), "color", "Reference palette", False, False, False, 1),
        ("_PaletteStrength", "Palette Strength", 0.0, "float", "Reference palette", False, False, False, 1),
        ("_TextureReference", "Original Cloth Luminance", .5, "float", "Reference palette", False, False, False, 1),
        ("_GoldColor", "Gold Embroidery Palette", (.72, .56, .34, 1), "color", "Reference palette", False, False, False, 1),
        ("_OutlineColor", "Outline Color", (.055, .065, .085, 1), "color", "Outline and M3", False, False, False, 1),
        ("_OutlinePixels", "Outline Pixels", 1.5, "float", "Outline and M3", False, False, False, 5),
        ("_OutlineScale", "Local Outline Scale", 1.0, "float", "Outline and M3", False, False, False, 1),
        ("_OutlineOnly", "Outline Renderer", 0.0, "float", "Outline and M3", True, False, False, 1),
        ("_Threshold", "M3 Dissolve", 0.0, "float", "Outline and M3", True, False, False, 1),
        ("_PrimaryColor", "M3 Primary", (1, 1, 1, 1), "color", "Outline and M3", True, False, False, 1),
        ("_SecondaryColor", "M3 Edge", (1, 1, 1, 1), "color", "Outline and M3", True, False, False, 1),
        ("_OrbisExplorerToon", "Explorer Toon Marker", 1.0, "float", "Outline and M3", True, False, False, 1),
        # Strengths remain zero in the shader; the importer supplies sampled palette colors and art tuning.
        ("_RimColor", "Reference Rim Color", (1, 1, 1, 1), "color", "Edge and highlights", False, False, True, 1),
        ("_RimStrength", "Rim Strength", 0.0, "float", "Edge and highlights", False, False, False, 1),
        ("_RimPower", "Rim Power", 3.0, "float", "Edge and highlights", False, False, False, 10),
        ("_SpecColor", "Reference Highlight Color", (1, 1, 1, 1), "color", "Edge and highlights", False, False, True, 1),
        ("_SpecStrength", "Metal Highlight Strength", 0.0, "float", "Edge and highlights", False, False, False, 1),
        ("_SpecPower", "Metal Highlight Power", 48.0, "float", "Edge and highlights", False, False, False, 128),
        ("_SkinSpecStrength", "Skin Highlight Strength", 0.0, "float", "Edge and highlights", False, False, False, 1),
        # Opt in on EX_Face only. Runtime keeps these world axes aligned with the animated head.
        ("_FaceLighting", "Analytic Face Lighting", 0.0, "float", "Face lighting", False, False, False, 1),
        ("_FaceForwardWS", "Face Forward WS", (0.0, 0.0, 1.0), "vector3", "Face lighting", False, False, False, 1),
        ("_FaceRightWS", "Face Right WS", (1.0, 0.0, 0.0), "vector3", "Face lighting", False, False, False, 1),
        # One-material weapon uses sampled linear vertex RGB. Existing character materials keep zero.
        ("_VertexColorStrength", "Vertex Color Strength", 0.0, "float", "Reference palette", False, False, False, 1),
    ]
    for index, (key, display, value, kind, category, hidden, main, hdr, maximum) in enumerate(definitions):
        p[key] = graph.property(key, display, value, kind, category, index * 130 - 900, hidden, main, hdr, maximum)

    pos_os = graph.geometry("Position OS", "PositionNode", 3, 0, -900, -900)
    normal_os = graph.geometry("Normal OS", "NormalVectorNode", 3, 0, -900, -680)
    pos_ws = graph.geometry("Position WS", "PositionNode", 3, 2, -850, 700)
    normal_ws = graph.geometry("Normal WS", "NormalVectorNode", 3, 2, -850, 920)
    uv = graph.geometry("Mesh UV0", "UVNode", 4, x=-850, y=250)
    vertex_color = graph.geometry("Vertex Color", "VertexColorNode", 4, x=-850, y=460)
    position_block = graph.block("Position", 3, True, "PositionMaterialSlot", m_Space=0)
    graph.block("Normal", 3, True, "NormalMaterialSlot", m_Space=0)
    graph.block("Tangent", 3, True, "TangentMaterialSlot", m_Space=0)
    original_ws = graph.custom_interpolator("ExplorerOriginalWS", pos_ws)

    hull_inputs = [("PositionOS", 3, pos_os, 0), ("NormalOS", 3, normal_os, 0),
                   ("OutlinePixels", 1, p["_OutlinePixels"], 0), ("OutlineOnly", 1, p["_OutlineOnly"], 0)]
    hull = graph.function("ExplorerHull", hull_inputs, [("Position", 3)], 250, -750, stage=1)
    graph.connect(hull, len(hull_inputs), position_block, 0)

    albedo_inputs = [("BaseMap", "texture", p["_BaseMap"], 0), ("UV", 2, uv, 0),
                     ("BaseColor", 4, p["_BaseColor"], 0), ("PaletteColor", 4, p["_PaletteColor"], 0),
                     ("PaletteStrength", 1, p["_PaletteStrength"], 0), ("TextureReference", 1, p["_TextureReference"], 0),
                     ("GoldColor", 4, p["_GoldColor"], 0), ("VertexColor", 4, vertex_color, 0),
                     ("VertexColorStrength", 1, p["_VertexColorStrength"], 0)]
    albedo = graph.function("ExplorerAlbedo", albedo_inputs, [("Albedo", 3), ("Alpha", 1)], -500, 50, stage=2)
    light_inputs = [("Albedo", 3, albedo, len(albedo_inputs)), ("WorldPosition", 3, pos_ws, 0),
                    ("WorldNormal", 3, normal_ws, 0), ("Ramp", "texture", p["_Ramp"], 0),
                    ("ShadowColor", 4, p["_ShadowColor"], 0), ("EmissionColor", 4, p["_EmissionColor"], 0),
                    ("UV", 2, uv, 0), ("FaceLighting", 1, p["_FaceLighting"], 0),
                    ("FaceForwardWS", 3, p["_FaceForwardWS"], 0), ("FaceRightWS", 3, p["_FaceRightWS"], 0)]
    lighting = graph.function("ExplorerThreeBand", light_inputs, [("Color", 3), ("Band", 1)], 200, 50, stage=2)
    highlight_inputs = [("LitColor", 3, lighting, len(light_inputs)), ("WorldPosition", 3, pos_ws, 0),
                        ("WorldNormal", 3, normal_ws, 0), ("RimColor", 4, p["_RimColor"], 0),
                        ("RimStrength", 1, p["_RimStrength"], 0), ("RimPower", 1, p["_RimPower"], 0),
                        ("SpecColor", 4, p["_SpecColor"], 0), ("SpecStrength", 1, p["_SpecStrength"], 0),
                        ("SpecPower", 1, p["_SpecPower"], 0), ("SkinSpecStrength", 1, p["_SkinSpecStrength"], 0)]
    highlights = graph.function("ExplorerHighlights", highlight_inputs, [("Color", 3)], 750, 50, stage=2)
    surface_inputs = [("LitColor", 3, highlights, len(highlight_inputs)), ("BaseAlpha", 1, albedo, len(albedo_inputs) + 1),
                      ("OriginalWorldPosition", 3, original_ws, 0), ("OutlineOnly", 1, p["_OutlineOnly"], 0),
                      ("OutlinePixels", 1, p["_OutlinePixels"], 0), ("OutlineColor", 4, p["_OutlineColor"], 0),
                      ("Threshold", 1, p["_Threshold"], 0), ("PrimaryColor", 4, p["_PrimaryColor"], 0),
                      ("SecondaryColor", 4, p["_SecondaryColor"], 0)]
    surface = graph.function("ExplorerSurface", surface_inputs, [("Color", 3), ("Alpha", 1), ("ClipThreshold", 1)],
                             1250, 180, stage=2)
    color = graph.block("BaseColor", 3, kind="ColorRGBMaterialSlot", m_ColorMode=0,
                        m_DefaultColor=rgba((.5, .5, .5, 1)))
    alpha = graph.block("Alpha", 1, default=1.0)
    clip = graph.block("AlphaClipThreshold", 1, default=.01)
    for offset, block in enumerate((color, alpha, clip)):
        graph.connect(surface, len(surface_inputs) + offset, block, 0)
    output = graph.save()
    print(json.dumps({"graph": str(output.relative_to(ROOT)), "graphGuid": uid("asset/" + output.name),
                      "hlslGuid": graph.include_guid, "nodes": len(graph.nodes), "properties": len(graph.properties),
                      "graphSha256": hashlib.sha256(output.read_bytes()).hexdigest()}, indent=2))


if __name__ == "__main__":
    build()

