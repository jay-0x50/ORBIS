"""Regenerate Orbis' editable URP 17 FoliageToon Shader Graph (Python stdlib only).

Run: python Tools/WorldDev/build_foliage_graph.py
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
OUT = ROOT / "Assets/Orbis/Game/World/Shaders"
NAME = "FoliageToon"
NS = uuid.UUID("f8d7b0c2-9b41-4f37-9693-d0151d668e8e")
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
        self.categories = {"Surface": [], "Wind": [], "Global World": [], "Per Draw Grass": [], "Billboard": []}

    def obj(self, key, kind, **fields):
        item = dict(m_SGVersion=0, m_Type="UnityEditor.ShaderGraph." + kind, m_ObjectId=uid("object/" + key))
        item.update(fields)
        self.objects.append(item)
        return item

    def slot(self, key, index, name, width=1, output=False, stage=3, kind=None, **fields):
        slot = self.obj(key, kind or f"Vector{width}MaterialSlot", m_Id=index, m_DisplayName=name,
                        m_SlotType=int(output), m_Hidden=False, m_ShaderOutputName=name,
                        m_StageCapability=stage)
        if kind in ("Texture2DMaterialSlot", "Texture2DInputMaterialSlot", "Texture2DArrayMaterialSlot", "Texture2DArrayInputMaterialSlot"):
            slot["m_BareResource"] = False
            if not output:
                slot.update(**({"m_TextureArray": {"m_SerializedTexture": "", "m_Guid": ""}} if kind == "Texture2DArrayInputMaterialSlot" else {"m_Texture": {"m_SerializedTexture": "", "m_Guid": ""}, "m_DefaultType": 0}))
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

    def property(self, reference, display, value, kind="float", category="Surface",
                 y=0, hidden=False, main=False, hdr=False, maximum=1.0, global_property=False):
        key = reference.lstrip("_")
        property_type = {"float": "Vector1ShaderProperty", "color": "ColorShaderProperty", "texture": "Texture2DShaderProperty",
                         "vector3": "Vector3ShaderProperty", "vector4": "Vector4ShaderProperty", "array": "Texture2DArrayShaderProperty"}[kind]
        prop = self.obj("property/" + key, "Internal." + property_type,
                        m_Guid={"m_GuidSerialized": str(uuid.UUID(uid("property-guid/" + key)))},
                        m_Name=display, m_DefaultRefNameVersion=1, m_RefNameGeneratedByDisplayName=display,
                        m_DefaultReferenceName=reference, m_OverrideReferenceName=reference,
                        m_GeneratePropertyBlock=not global_property, m_UseCustomSlotLabel=False, m_CustomSlotLabel="",
                        m_DismissedVersion=0, m_Precision=1, overrideHLSLDeclaration=global_property,
                        hlslDeclarationOverride=1 if global_property else 0, m_Hidden=hidden, m_PerRendererData=False,
                        m_customAttributes=[])
        if kind == "float":
            prop.update(m_SGVersion=1, m_Value=value, m_FloatType=1,
                        m_RangeValues={"x": 0.0, "y": maximum}, m_LiteralFloatMode=False)
            slot = self.slot("property-slot/" + key, 0, "Out", output=True)
        elif kind == "color":
            prop.update(m_SGVersion=3, m_Value=rgba(value), isMainColor=main, m_ColorMode=int(hdr))
            slot = self.slot("property-slot/" + key, 0, "Out", 4, output=True)
        elif kind in ("vector3", "vector4"):
            # URP 17 VectorShaderProperty serializes a Vector4 value, even for float3 declarations.
            prop.update(m_SGVersion=1, m_Value=vector(4, (*value, 0.0) if kind == "vector3" else value))
            slot = self.slot("property-slot/" + key, 0, "Out", 3 if kind == "vector3" else 4, output=True)
        elif kind == "array":
            prop.update(m_Value={"m_SerializedTexture": "", "m_Guid": ""}, isHDR=False, m_Modifiable=True)
            slot = self.slot("property-slot/" + key, 0, "Out", output=True, kind="Texture2DArrayMaterialSlot")
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
            slots.append(self.slot(name + "/input/" + label, index, label, width if isinstance(width, int) else 1,
                                   stage=stage, kind={"texture": "Texture2DInputMaterialSlot", "array": "Texture2DArrayInputMaterialSlot"}.get(width)))
        for index, (label, width) in enumerate(outputs, len(inputs)):
            slots.append(self.slot(name + "/output/" + label, index, label, width, output=True, stage=stage))
        node = self.node("function/" + name, "CustomFunctionNode", name + " (Custom Function)", slots, x, y,
                         m_SourceType=0, m_FunctionName=name, m_FunctionSource=self.include_guid,
                         m_FunctionSourceUsePragmas=True, m_FunctionBody="")
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
                      m_AlphaMode=0, m_RenderFace=0, m_AlphaClip=True, m_CastShadows=True, m_ReceiveShadows=True,
                      m_DisableTint=False, m_AdditionalMotionVectorMode=1, m_AlembicMotionVectors=False,
                      m_SupportsLODCrossFade=True, m_CustomEditorGUI="", m_SupportVFX=False)
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
                     m_Path="Orbis/World", m_GraphPrecision=0, m_PreviewMode=2, m_OutputNode=ref(""),
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
    assert {"_BaseArray", "_Ramp", "_BillboardMode", "_OrbisFoliage", "_OrbisGrassCameraPosition"}.issubset(props)
    for reference in ("_OrbisWindDirection", "_OrbisWindParams", "_OrbisPlayerPosition", "_OrbisPlayerRadius", "_OrbisBiomeAmbient"):
        prop = next(x for x in data if x.get("m_OverrideReferenceName") == reference)
        assert not prop["m_GeneratePropertyBlock"] and prop["hlslDeclarationOverride"] == 1



def build():
    OUT.mkdir(parents=True, exist_ok=True)
    graph = Graph(write_meta(OUT / "FoliageLighting.hlsl", "include"))
    p = {}
    # Global values are never emitted in the material property block; per-draw grass values are.
    definitions = [
        ("_BaseArray", "Source Texture Array", None, "array", "Surface", False, False, False, 1, False),
        ("_BaseColor", "Source Tint", (1, 1, 1, 1), "color", "Surface", False, False, False, 1, False),
        ("_ShadowColor", "Shadow Color", (.55, .55, .55, 1), "color", "Surface", False, False, False, 1, False),
        ("_Ramp", "Three Band Ramp", None, "texture", "Surface", False, False, False, 1, False),
        ("_Cutoff", "Source Alpha Cutoff", .2, "float", "Surface", False, False, False, 1, False),
        ("_LeafSaturation", "Leaf Saturation", 1.0, "float", "Surface", False, False, False, 1, False),
        ("_LeafShadowLift", "Dark Leaf Luminance Lift", 0.0, "float", "Surface", False, False, False, .2, False),
        ("_LeafTint", "Sage Leaf Palette", (1, 1, 1, 1), "color", "Surface", False, False, False, 1, False),
        ("_LeafTintStrength", "Green Leaf Palette Blend", 0.0, "float", "Surface", False, False, False, 1, False),
        ("_AmbientStrength", "Leaf Sky Fill", 0.0, "float", "Surface", False, False, False, 1, False),
        ("_WindStrength", "Branch Sway Metres", .22, "float", "Wind", False, False, False, 1, False),
        ("_FlutterStrength", "Leaf Flutter Strength", .8, "float", "Wind", False, False, False, 2, False),
        ("_GrassBend", "Grass Bending and Distance Fade", 0.0, "float", "Wind", False, False, False, 1, False),
        ("_BillboardAtlas", "Eight View Billboard Atlas", None, "texture", "Billboard", False, False, False, 1, False),
        ("_BillboardMode", "Billboard Mode", 0.0, "float", "Billboard", False, False, False, 1, False),
        ("_OrbisWindDirection", "World Wind Direction", (1.0, 0.0, 0.0), "vector3", "Global World", False, False, False, 1, True),
        ("_OrbisWindParams", "Main Turbulence Pulse Frequency", (0.0, 0.0, 0.0, 0.0), "vector4", "Global World", False, False, False, 1, True),
        ("_OrbisPlayerPosition", "Controlled Pawn Position", (0.0, 0.0, 0.0), "vector3", "Global World", False, False, False, 1, True),
        ("_OrbisPlayerRadius", "Pawn Grass Bend Radius", 0.0, "float", "Global World", False, False, False, 3, True),
        ("_OrbisBiomeAmbient", "World Biome Ambient Multiplier", (1, 1, 1, 1), "color", "Global World", False, False, False, 1, True),
        ("_OrbisGrassCameraPosition", "Grass Draw Camera Position", (0.0, 0.0, 0.0), "vector3", "Per Draw Grass", False, False, False, 1, False),
        ("_OrbisGrassFadeStart", "Grass Fade Start Metres", 75.0, "float", "Per Draw Grass", False, False, False, 200, False),
        ("_OrbisGrassFadeEnd", "Grass Fade End Metres", 100.0, "float", "Per Draw Grass", False, False, False, 300, False),
        ("_OrbisFoliage", "Foliage Shader Marker", 1.0, "float", "Surface", True, False, False, 1, False),
    ]
    for index, (key, display, value, kind, category, hidden, main, hdr, maximum, global_property) in enumerate(definitions):
        p[key] = graph.property(key, display, value, kind, category, index * 130 - 900,
                                hidden, main, hdr, maximum, global_property)

    pos_os = graph.geometry("Position OS", "PositionNode", 3, 0, -950, -800)
    normal_os = graph.geometry("Normal OS", "NormalVectorNode", 3, 0, -950, -620)
    pos_ws = graph.geometry("Position WS", "PositionNode", 3, 2, -900, 450)
    normal_ws = graph.geometry("Normal WS", "NormalVectorNode", 3, 2, -900, 650)
    uv = graph.geometry("Original UV0", "UVNode", 4, x=-950, y=50)
    uv1 = graph.geometry("Slice and Wind UV1", "UVNode", 4, x=-950, y=250)
    uv1["m_OutputChannel"] = 1
    uv2 = graph.geometry("Flutter and Height UV2", "UVNode", 4, x=-950, y=450)
    uv2["m_OutputChannel"] = 2
    time = graph.node("geometry/Time", "TimeNode", "Time",
                     [graph.slot("Time/" + str(i), i, label, output=True) for i, label in
                      enumerate(["Time", "Sine Time", "Cosine Time", "Delta Time", "Smooth Delta"])],
                     -950, -1100)
    position = graph.block("Position", 3, True, "PositionMaterialSlot", m_Space=0)
    normal = graph.block("Normal", 3, True, "NormalMaterialSlot", m_Space=0)
    graph.block("Tangent", 3, True, "TangentMaterialSlot", m_Space=0)
    wind_inputs = [
        ("PositionOS", 3, pos_os, 0), ("NormalOS", 3, normal_os, 0),
        ("UV1", 4, uv1, 0), ("UV2", 4, uv2, 0), ("Time", 1, time, 0),
        ("WindDirection", 3, p["_OrbisWindDirection"], 0), ("WindParams", 4, p["_OrbisWindParams"], 0),
        ("PlayerPosition", 3, p["_OrbisPlayerPosition"], 0), ("PlayerRadius", 1, p["_OrbisPlayerRadius"], 0),
        ("WindStrength", 1, p["_WindStrength"], 0), ("FlutterStrength", 1, p["_FlutterStrength"], 0),
        ("GrassBend", 1, p["_GrassBend"], 0), ("BillboardMode", 1, p["_BillboardMode"], 0),
    ]
    wind = graph.function("FoliageWind", wind_inputs, [("Position", 3), ("Normal", 3)], 150, -700, stage=1)
    graph.connect(wind, len(wind_inputs), position, 0)
    graph.connect(wind, len(wind_inputs) + 1, normal, 0)
    surface_inputs = [
        ("BaseArray", "array", p["_BaseArray"], 0), ("BillboardAtlas", "texture", p["_BillboardAtlas"], 0),
        ("UV", 2, uv, 0), ("UV1", 4, uv1, 0), ("UV2", 4, uv2, 0),
        ("WorldPosition", 3, pos_ws, 0), ("WorldNormal", 3, normal_ws, 0),
        ("BaseColor", 4, p["_BaseColor"], 0), ("ShadowColor", 4, p["_ShadowColor"], 0),
        ("Ramp", "texture", p["_Ramp"], 0), ("Cutoff", 1, p["_Cutoff"], 0),
        ("BillboardMode", 1, p["_BillboardMode"], 0), ("GrassBend", 1, p["_GrassBend"], 0),
        ("GrassCameraPosition", 3, p["_OrbisGrassCameraPosition"], 0),
        ("GrassFadeStart", 1, p["_OrbisGrassFadeStart"], 0), ("GrassFadeEnd", 1, p["_OrbisGrassFadeEnd"], 0),
        ("LeafSaturation", 1, p["_LeafSaturation"], 0), ("LeafShadowLift", 1, p["_LeafShadowLift"], 0),
        ("LeafTint", 4, p["_LeafTint"], 0), ("LeafTintStrength", 1, p["_LeafTintStrength"], 0),
        ("AmbientStrength", 1, p["_AmbientStrength"], 0), ("BiomeAmbient", 4, p["_OrbisBiomeAmbient"], 0),
    ]
    surface = graph.function("FoliageLighting", surface_inputs,
                             [("Color", 3), ("Alpha", 1), ("ClipThreshold", 1)], 400, 100, stage=2)
    color = graph.block("BaseColor", 3, kind="ColorRGBMaterialSlot", m_ColorMode=0,
                        m_DefaultColor=rgba((.5, .5, .5, 1)))
    alpha = graph.block("Alpha", 1, default=1.0)
    clip = graph.block("AlphaClipThreshold", 1, default=.2)
    for offset, block in enumerate((color, alpha, clip)):
        graph.connect(surface, len(surface_inputs) + offset, block, 0)
    output = graph.save()
    print(json.dumps({"graph": str(output.relative_to(ROOT)), "graphGuid": uid("asset/" + output.name),
                      "hlslGuid": graph.include_guid, "nodes": len(graph.nodes), "properties": len(graph.properties),
                      "graphSha256": hashlib.sha256(output.read_bytes()).hexdigest()}, indent=2))


if __name__ == "__main__":
    build()
