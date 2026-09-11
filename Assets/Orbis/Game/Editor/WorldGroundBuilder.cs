using System;
using System.IO;
using Orbis.Game.World;
using UnityEditor;
using UnityEngine;

namespace Orbis.Game.Editor
{
    /// <summary>Step 2 ground art only. Preserve authored elevation, collision, roads and the original six biome masks.</summary>
    public static class WorldGroundBuilder
    {
        public const string Root="Assets/Orbis/Game/World/Ground";
        public const string RockMaterialPath=Root+"/Materials/MossRock.mat";
        public const string TerrainShaderName="Orbis/World/TerrainToon";
        public static Material RockMaterial=>AssetDatabase.LoadAssetAtPath<Material>(RockMaterialPath);
        static readonly string[] Names={"Meadow","Earth","Stone","Sand","VolcanicAsh","Trail","Moss"};

        /// <summary>Step 4 material-only styling. Existing sculpting, seven-way splat masks and collision stay untouched.</summary>
        public static void ApplyStyle(Terrain[] terrains)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Ground styling is authored outside Play Mode.");
            if(terrains==null||terrains.Length==0)throw new ArgumentException("Provide the resident island terrains.");
            foreach(var terrain in terrains)
                if(terrain==null||terrain.terrainData==null||terrain.terrainData.alphamapLayers<6||terrain.terrainData.alphamapLayers>7)
                    throw new InvalidOperationException("Ground styling requires the six/seven existing island layers.");
            foreach(string name in new[]{TerrainShaderName,"Hidden/Orbis/World/TerrainAdd","Hidden/Orbis/World/TerrainBase"})
            {
                var shader=Shader.Find(name);
                if(shader==null||ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Import a valid terrain shader: "+name);
            }
            var toon=Shader.Find(TerrainShaderName);
            Directory.CreateDirectory(Root+"/Materials");
            var visitedLayers=new System.Collections.Generic.HashSet<TerrainLayer>();
            var visitedMaterials=new System.Collections.Generic.HashSet<Material>();
            // Provisional art tints: restrained meadow/moss greens, warm soil and cool ivory/slate stone.
            // These multiply existing albedos; no repaint, layer replacement or loss of biome transitions.
            // Meadow/Moss use slightly less red and more blue after the first real render showed a yellow-olive cast.
            var tint=new[]{new Vector4(.72f,.92f,.98f,1),new Vector4(.90f,.83f,.70f,1),new Vector4(.86f,.89f,.96f,1),
                new Vector4(1.03f,.95f,.79f,1),new Vector4(.42f,.40f,.45f,1),new Vector4(.85f,.77f,.65f,1),new Vector4(.72f,.93f,.95f,1)};
            foreach(var terrain in terrains)
            {
                foreach(var layer in terrain.terrainData.terrainLayers)
                {
                    if(layer==null)throw new InvalidOperationException("A terrain layer reference is missing.");
                    if(!visitedLayers.Add(layer))continue;
                    // Unity's default DiffuseAlphaChannel treats our opaque texture alpha (1) as smoothness 1,
                    // overriding layer.smoothness=0. This was the white, view-dependent Step 3 ground glare.
                    layer.smoothnessSource=TerrainLayerSmoothnessSource.ConstantOnly;
                    layer.smoothness=0;layer.metallic=0;
                    int index=Array.IndexOf(Names,layer.name);
                    if(index>=0)layer.diffuseRemapMax=tint[index];
                    EditorUtility.SetDirty(layer);
                }
                Material material=terrain.materialTemplate;
                if(material==null)
                {
                    const string path=Root+"/Materials/TerrainToon.mat";
                    material=AssetDatabase.LoadAssetAtPath<Material>(path);
                    if(material==null){material=new Material(toon){name="Continental Terrain Toon"};AssetDatabase.CreateAsset(material,path);}
                    terrain.materialTemplate=material;
                }
                if(visitedMaterials.Add(material))
                {
                    material.shader=toon;material.enableInstancing=true;
                    material.SetFloat("_EnableInstancedPerPixelNormal",1);
                    material.EnableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
                    EditorUtility.SetDirty(material);
                }
                // Refresh the engine's generated distant color map after its layer tint/smoothness changed.
                // No SetHeights/SetAlphamaps/SetHoles or terrain-layer assignment is performed here.
                terrain.terrainData.SetBaseMapDirty();terrain.Flush();EditorUtility.SetDirty(terrain);
            }
            var rock=RockMaterial;
            if(rock==null)throw new InvalidOperationException("Complete Step 2 MossRock before world styling.");
            rock.SetColor("_StoneColor",new Color(.86f,.89f,.96f));
            rock.SetColor("_MossColor",new Color(.72f,.93f,.95f));
            rock.SetFloat("_AmbientStrength",.55f);EditorUtility.SetDirty(rock);
            AssetDatabase.SaveAssets();
            Debug.Log("ORBIS_WORLD_GROUND_STYLE: constant-zero smoothness, shared diffuse three-band forward/add/basemap lighting; terrain heights, holes and splat weights retained.");
        }

        public static void Apply(Terrain[] terrains)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Ground art is authored outside Play Mode.");
            if(terrains==null||terrains.Length==0)throw new ArgumentException("Provide the existing terrain tiles.");
            foreach(var terrain in terrains)
                if(terrain==null||terrain.terrainData==null||
                    (terrain.terrainData.alphamapLayers!=6&&terrain.terrainData.alphamapLayers!=7))
                    throw new InvalidOperationException("Ground art requires the existing six island layers, optionally plus Moss.");
            foreach(string name in new[]{"Meadow","Earth","Stone","Moss"})
                if(!File.Exists(Root+"/Textures/"+name+".png"))throw new FileNotFoundException("Ground source texture is missing: "+name);
            Directory.CreateDirectory(Root+"/TerrainLayers");Directory.CreateDirectory(Root+"/Materials");AssetDatabase.Refresh();
            var meadow=Import("Meadow");var earth=Import("Earth");var stone=Import("Stone");var moss=Import("Moss");
            var layers=BuildLayers(meadow,earth,stone,moss);BuildRockMaterial(stone,moss);
            foreach(var terrain in terrains)
            {
                var data=terrain.terrainData;int n=data.alphamapResolution;
                var source=data.GetAlphamaps(0,0,n,n);
                var painted=PaintMoss(data,terrain.transform.position,source);
                data.terrainLayers=layers;data.SetAlphamaps(0,0,painted);
                // No SetHeights, transforms, TerrainCollider or traversal component modifications.
                terrain.Flush();EditorUtility.SetDirty(data);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("ORBIS_WORLD_GROUND: four shared 1024px BC7/Mirror albedos, seven layers, protected road weights and terrain heights retained.");
        }

        static Texture2D Import(string name)
        {
            string path=Root+"/Textures/"+name+".png";
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer==null)throw new InvalidOperationException("Texture importer is missing: "+path);
            importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;
            importer.maxTextureSize=1024;importer.isReadable=false;importer.mipmapEnabled=true;
            importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=4;
            // The generated source is not claimed to be seamless. Mirroring joins each edge to its reflection.
            importer.wrapMode=TextureWrapMode.Mirror;importer.npotScale=TextureImporterNPOTScale.ToNearest;
            importer.alphaSource=TextureImporterAlphaSource.None;importer.alphaIsTransparency=false;
            importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.crunchedCompression=false;
            var platform=importer.GetPlatformTextureSettings("Standalone");platform.name="Standalone";
            platform.overridden=true;platform.maxTextureSize=1024;platform.format=TextureImporterFormat.BC7;
            platform.textureCompression=TextureImporterCompression.CompressedHQ;platform.compressionQuality=100;
            importer.SetPlatformTextureSettings(platform);importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static TerrainLayer[] BuildLayers(Texture2D meadow,Texture2D earth,Texture2D stone,Texture2D moss)
        {
            var textures=new[]{meadow,earth,stone,earth,stone,earth,moss};
            // Art defaults in metres. An even number of repeats per 1000m tile keeps Mirror phase continuous
            // at all four tile edges while sharing the same TerrainLayer assets.
            float[] metres={5,10,20,20,25,10,5};
            var tint=new[]{new Vector4(.94f,1,.94f,1),Vector4.one,new Vector4(.92f,.96f,1,1),
                new Vector4(1.12f,1.10f,1.03f,1),new Vector4(.42f,.40f,.39f,1),new Vector4(.91f,.91f,.90f,1),Vector4.one};
            var layers=new TerrainLayer[7];
            for(int i=0;i<layers.Length;i++)
            {
                string path=Root+"/TerrainLayers/"+Names[i]+".terrainlayer";
                var layer=AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                if(layer==null){layer=new TerrainLayer{name=Names[i]};AssetDatabase.CreateAsset(layer,path);}
                layer.diffuseTexture=textures[i];layer.normalMapTexture=null;layer.maskMapTexture=null;
                layer.tileSize=Vector2.one*metres[i];layer.tileOffset=Vector2.zero;
                layer.metallic=0;layer.smoothness=0;layer.smoothnessSource=TerrainLayerSmoothnessSource.ConstantOnly;layer.normalScale=1;
                layer.diffuseRemapMin=Vector4.zero;layer.diffuseRemapMax=tint[i];
                layers[i]=layer;EditorUtility.SetDirty(layer);
            }
            return layers;
        }

        static void BuildRockMaterial(Texture2D stone,Texture2D moss)
        {
            var shader=Shader.Find("Orbis/World/MossRock");
            if(shader==null)throw new InvalidOperationException("Import the Orbis/World/MossRock shader before building ground art.");
            if(ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("MossRock has shader compilation errors.");
            var material=RockMaterial;
            if(material==null){material=new Material(shader){name="Moss Rock"};AssetDatabase.CreateAsset(material,RockMaterialPath);}
            material.shader=shader;material.enableInstancing=true;
            material.SetTexture("_StoneMap",stone);material.SetTexture("_MossMap",moss);
            material.SetColor("_StoneColor",Color.white);material.SetColor("_MossColor",Color.white);
            // Metre-based triplanar scale and normal thresholds are provisional art values.
            material.SetFloat("_TextureScale",.22f);material.SetFloat("_TriplanarSharpness",4f);
            material.SetFloat("_MossStart",.36f);material.SetFloat("_MossEnd",.83f);material.SetFloat("_MossCoverage",.78f);
            material.SetFloat("_AmbientStrength",.72f);EditorUtility.SetDirty(material);
        }

        static float[,,] PaintMoss(TerrainData data,Vector3 origin,float[,,] source)
        {
            int n=source.GetLength(0),channels=source.GetLength(2);var result=new float[n,n,7];
            for(int z=0;z<n;z++)for(int x=0;x<n;x++)
            {
                float u=x/(n-1f),v=z/(n-1f),wx=origin.x+u*data.size.x,wz=origin.z+v*data.size.z;
                float grass=source[z,x,0],earth=source[z,x,1],vegetation=grass+earth;
                // Reconstruct the pre-Moss grass/earth proportions, so running Step 2 twice does not accumulate it.
                if(channels==7&&source[z,x,6]>0&&vegetation>.000001f)
                {
                    float restore=(vegetation+source[z,x,6])/vegetation;grass*=restore;earth*=restore;
                }
                else if(channels==7&&vegetation<=.000001f)grass+=source[z,x,6];
                float height=origin.y+data.GetInterpolatedHeight(u,v),slope=data.GetSteepness(u,v);
                var biome=WorldBiome.Sample(wx,wz);
                float wetness=.23f*biome.Zephyr+.43f*biome.Voltheim+.35f*biome.Teluna+.13f*biome.Granite+.07f*biome.Agnia;
                float patch=Smooth(.36f,.68f,Mathf.PerlinNoise(wx*.016f+81,wz*.016f+29));
                float exposure=(1-Smooth(24,48,slope))*(1-Smooth(95,165,height))*Smooth(2,15,height);
                float clearSite=Smooth(WorldTerrainUpgrade.ProtectedPadRadius,105,IslandTerrainBuilder.SiteDistance(wx,wz));
                float fraction=Mathf.Clamp01(wetness*patch*exposure*clearSite*1.6f);
                fraction=Mathf.Min(.55f,fraction)*(1-Mathf.Clamp01(source[z,x,5]));
                result[z,x,0]=grass*(1-fraction);result[z,x,1]=earth*(1-fraction);
                for(int layer=2;layer<6;layer++)result[z,x,layer]=source[z,x,layer];
                result[z,x,6]=(grass+earth)*fraction;
                float sum=0;for(int layer=0;layer<7;layer++)sum+=result[z,x,layer];
                if(sum>.000001f)for(int layer=0;layer<7;layer++)result[z,x,layer]/=sum;
                else result[z,x,0]=1;
            }
            return result;
        }
        static float Smooth(float a,float b,float x)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,x));
    }
}
