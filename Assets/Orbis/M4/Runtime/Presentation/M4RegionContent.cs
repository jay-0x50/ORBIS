using System;
using Orbis.M0;
using Orbis.M1;
using Orbis.M2;
using Orbis.M3;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.M4
{
    /// <summary>Composes the existing three-statue and three-target models with region-specific data.</summary>
    public sealed class M4RegionContent : MonoBehaviour
    {
        private M4RegionId region;
        private MaterialPropertyBlock tint;
        private Renderer[] statueBodies;
        private Renderer[] challengeBodies;
        private Renderer chestBody;
        public FieldElementPuzzle Puzzle { get; private set; }
        public ChallengeRoom Challenge { get; private set; }
        public ElementalActor[] Statues { get; private set; }
        public ElementalActor[] Targets { get; private set; }
        public GameObject BossObject { get; private set; }
        public ElementType PuzzleElement => M4RegionCatalog.Get(region).Element;
        public static ReactionType RequiredReaction(M4RegionId id)
        {
            switch(id)
            {
                case M4RegionId.Teluna: return ReactionType.ElectroCharged;
                case M4RegionId.Zephyr: return ReactionType.Swirl;
                case M4RegionId.Granite: return ReactionType.Crystallize;
                case M4RegionId.Voltheim: return ReactionType.Overload;
                default: return ReactionType.Vaporize;
            }
        }

        public void Build(ElementalReactionManager manager, M4RegionId id, M4RegionLayout layout, M2SceneBootstrap legacy)
        {
            region = id; tint = new MaterialPropertyBlock();
            if(legacy != null)
            {
                Puzzle = legacy.FieldPuzzle; Challenge = legacy.Challenge;
                Statues = Puzzle.GetComponentsInChildren<ElementalActor>(true);
                Targets = Challenge.GetComponentsInChildren<ElementalActor>(true);
                Array.Sort(Statues, (a,b)=>string.CompareOrdinal(a.SourceId,b.SourceId));
                Array.Sort(Targets, (a,b)=>string.CompareOrdinal(a.SourceId,b.SourceId));
                // M2 Ready state disables target colliders; restore their authored capsule before reconfiguration.
                foreach(var target in Targets) target.GetComponent<CapsuleCollider>().enabled = true;
            }
            else
            {
                Statues = new ElementalActor[3]; Targets = new ElementalActor[3];
                statueBodies = new Renderer[3]; challengeBodies = new Renderer[3];
                var puzzleRoot = new GameObject("M4 Ordered Element Puzzle"); puzzleRoot.transform.SetParent(transform,false);
                var roomRoot = new GameObject("M4 Reaction Challenge"); roomRoot.transform.SetParent(transform,false);
                for(int i=0;i<3;i++)
                {
                    Statues[i]=Target(id+" Statue "+(i+1),puzzleRoot.transform,layout.PuzzleStatuePositions[i],i+1,out statueBodies[i]);
                    Targets[i]=Target(id+" Challenge "+(i+1),roomRoot.transform,layout.ChallengeTargets[i],i+1,out challengeBodies[i]);
                }
                Puzzle = puzzleRoot.AddComponent<FieldElementPuzzle>();
                Challenge = roomRoot.AddComponent<ChallengeRoom>();
                chestBody = Part("Field Reward Chest",PrimitiveType.Cube,transform,layout.Chest+Vector3.up*.4f,new Vector3(1.5f,.8f,1.1f),
                    new Color(.45f,.29f,.13f),false).GetComponent<Renderer>();
            }
            // The mechanics remain M2's defaults; region element/reaction and reward IDs are data.
            // M4 replay keeps daily objectives possible while the same session's material reward stays one-shot.
            Puzzle.Configure(manager,Statues,RewardInventory.Session,"m4."+id+".puzzle",PuzzleElement,true);
            Challenge.Configure(manager,Targets,RewardInventory.Session,"m4."+id+".challenge",RequiredReaction(id),true);
            Puzzle.Changed += Refresh; Challenge.Changed += Refresh; Refresh();
            CreateNpc(layout.Npc);
            Part("Regional Transit Obelisk",PrimitiveType.Cube,transform,layout.Portal+Vector3.up*1.5f,new Vector3(.7f,3f,.7f),
                M3Palette.Primary(PuzzleElement),false);
            Part("Survey Marker",PrimitiveType.Cylinder,transform,layout.Portal+Vector3.forward*3f+Vector3.up*.03f,
                new Vector3(1.5f,.03f,1.5f),M3Palette.Secondary(PuzzleElement),false);
            BossObject = BuildBoss(layout.BossCenter);
        }

        /// <summary>Connect existing scene actors and visuals; preserve authored transforms, colliders and hierarchy.</summary>
        public void Bind(ElementalReactionManager manager, M4RegionId id, M4AuthoredRegion authored)
        {
            if (manager == null || authored == null) throw new ArgumentNullException("Authored content requires a manager and scene bindings.");
            authored.Validate(); M4RegionCatalog.Get(id);
            if (Puzzle != null) Puzzle.Changed -= Refresh;
            if (Challenge != null) Challenge.Changed -= Refresh;
            region = id;
            tint = new MaterialPropertyBlock();
            Statues = (ElementalActor[])authored.Statues.Clone();
            Targets = (ElementalActor[])authored.Targets.Clone();
            statueBodies = (Renderer[])authored.StatueRenderers.Clone();
            challengeBodies = (Renderer[])authored.ChallengeRenderers.Clone();
            chestBody = authored.ChestRenderer;
            Puzzle = authored.Puzzle;
            Challenge = authored.Challenge;
            BossObject = authored.BossObject;
            for (int i = 0; i < 3; i++)
            {
                // Keep authored IDs; supply legacy-compatible IDs only when an editor object has no identity yet.
                string statueId = string.IsNullOrWhiteSpace(Statues[i].SourceId) ? id + " Statue " + (i + 1) : Statues[i].SourceId;
                string targetId = string.IsNullOrWhiteSpace(Targets[i].SourceId) ? id + " Challenge " + (i + 1) : Targets[i].SourceId;
                Statues[i].Configure(statueId, ElementType.None, ActorTeam.Enemy);
                Targets[i].Configure(targetId, ElementType.None, ActorTeam.Enemy);
                Statues[i].IsOnField = true;
            }
            Puzzle.Configure(manager,Statues,RewardInventory.Session,"m4."+id+".puzzle",PuzzleElement,true);
            Challenge.Configure(manager,Targets,RewardInventory.Session,"m4."+id+".challenge",RequiredReaction(id),true);
            Puzzle.Changed += Refresh;
            Challenge.Changed += Refresh;
            Refresh();
        }

        private ElementalActor Target(string name,Transform parent,Vector3 position,int ordinal,out Renderer body)
        {
            var root=new GameObject(name); root.transform.SetParent(parent,false); root.transform.position=position; root.layer=9;
            var collider=root.AddComponent<CapsuleCollider>(); collider.center=Vector3.up; collider.height=2f; collider.radius=.42f;
            body=Part("Statue Body",PrimitiveType.Cube,root.transform,Vector3.up*.9f,new Vector3(.65f,1.6f,.65f),Color.gray,false).GetComponent<Renderer>();
            Part("Statue Head",PrimitiveType.Sphere,root.transform,Vector3.up*1.9f,Vector3.one*.5f,Color.gray,false);
            for(int i=0;i<ordinal;i++)
                Part("Order Mark "+i,PrimitiveType.Cube,root.transform,new Vector3((i-(ordinal-1)*.5f)*.16f,1.05f,-.34f),
                    new Vector3(.08f,.36f,.04f),M3Palette.Secondary(PuzzleElement),false);
            root.AddComponent<TrainingDummy>();
            var actor=root.AddComponent<ElementalActor>(); actor.Configure(name,ElementType.None,ActorTeam.Enemy);
            return actor;
        }

        private void CreateNpc(Vector3 position)
        {
            var npc = new GameObject("Commission NPC / "+M4RegionCatalog.Get(region).DisplayName);
            npc.transform.SetParent(transform,false); npc.transform.position=position;
            Part("NPC Coat",PrimitiveType.Capsule,npc.transform,Vector3.up*.9f,new Vector3(.65f,.75f,.65f),M3Palette.Primary(PuzzleElement),false);
            Part("NPC Head",PrimitiveType.Sphere,npc.transform,Vector3.up*1.8f,Vector3.one*.45f,new Color(.83f,.72f,.56f),false);
            Part("Request Placard",PrimitiveType.Cube,npc.transform,new Vector3(.7f,1.2f,0),new Vector3(.65f,.85f,.12f),new Color(.88f,.84f,.66f),false);
        }

        private GameObject BuildBoss(Vector3 position)
        {
            var root=new GameObject(region+" Elemental Field Guardian");
            root.transform.SetParent(transform,false); root.transform.position=position; root.layer=9;
            var collider=root.AddComponent<CapsuleCollider>(); collider.center=Vector3.up*1.25f; collider.height=2.5f; collider.radius=.65f;
            root.AddComponent<TrainingDummy>();
            var actor=root.AddComponent<ElementalActor>(); actor.Configure("boss."+region,PuzzleElement,ActorTeam.Enemy);
            Color primary=M3Palette.Primary(PuzzleElement),secondary=M3Palette.Secondary(PuzzleElement);
            switch(region)
            {
                case M4RegionId.Teluna:
                    Part("Tidal Guardian Shell",PrimitiveType.Sphere,root.transform,Vector3.up*1.5f,new Vector3(2.4f,1.7f,2.1f),primary,false);
                    for(int i=0;i<4;i++) Part("Coral Fin "+i,PrimitiveType.Cube,root.transform,
                        Quaternion.Euler(0,i*90,0)*new Vector3(1.1f,1.2f,0),new Vector3(.3f,.9f,.8f),secondary,false);
                    break;
                case M4RegionId.Zephyr:
                    Part("Wind Guardian Core",PrimitiveType.Sphere,root.transform,Vector3.up*1.6f,Vector3.one*1.6f,primary,false);
                    for(int i=0;i<4;i++) { var sail=Part("Guardian Sail "+i,PrimitiveType.Cube,root.transform,
                        Quaternion.Euler(0,0,i*90)*new Vector3(0,.9f,0)+Vector3.up*1.4f,new Vector3(.35f,1.3f,.25f),secondary,false); sail.transform.localRotation=Quaternion.Euler(0,0,i*90+25); }
                    break;
                case M4RegionId.Granite:
                    Part("Mining Colossus Torso",PrimitiveType.Cube,root.transform,Vector3.up*1.4f,new Vector3(1.9f,1.8f,1.2f),primary,false);
                    Part("Colossus Head",PrimitiveType.Cube,root.transform,Vector3.up*2.65f,Vector3.one*.85f,secondary,false);
                    for(int i=-1;i<=1;i+=2) Part("Stone Fist",PrimitiveType.Cube,root.transform,new Vector3(i*1.35f,1.2f,0),Vector3.one*.85f,primary,false);
                    break;
                case M4RegionId.Voltheim:
                    Part("Storm Golem Body",PrimitiveType.Cylinder,root.transform,Vector3.up*1.3f,new Vector3(1.5f,1.1f,1.5f),primary,false);
                    for(int i=-1;i<=1;i+=2) Part("Conductor Rod",PrimitiveType.Cylinder,root.transform,new Vector3(i*.85f,2f,0),new Vector3(.17f,1.2f,.17f),secondary,false);
                    Part("Lightning Crown",PrimitiveType.Sphere,root.transform,Vector3.up*2.5f,Vector3.one*.7f,secondary,false);
                    break;
                default:
                    Part("Magma Guardian Body",PrimitiveType.Capsule,root.transform,Vector3.up*1.25f,new Vector3(1.7f,1.15f,1.7f),primary,false);
                    for(int i=0;i<3;i++) Part("Basalt Crest "+i,PrimitiveType.Cube,root.transform,new Vector3((i-1)*.6f,2.45f,0),
                        new Vector3(.45f,.7f,.65f),secondary,false);
                    break;
            }
            Part("Weakness Core",PrimitiveType.Sphere,root.transform,new Vector3(0,1.3f,-.92f),Vector3.one*.45f,
                M3Palette.Primary(M4RegionCatalog.Get(region).Weakness),false);
            return root;
        }

        private void Refresh()
        {
            if(statueBodies!=null)
                for(int i=0;i<3;i++) Tint(statueBodies[i],Puzzle.Model.IsLit(i)?M3Palette.Primary(PuzzleElement):Color.gray);
            if(challengeBodies!=null)
                for(int i=0;i<3;i++) Tint(challengeBodies[i],Challenge.IsDefeated(i)?new Color(.2f,.7f,.35f):
                    Targets[i].AuraElement!=ElementType.None?M3Palette.Primary(Targets[i].AuraElement):Color.gray);
            if(chestBody!=null) Tint(chestBody,Puzzle.Model.IsUnlocked?M3Palette.Secondary(PuzzleElement):new Color(.45f,.29f,.13f));
        }
        private void LateUpdate() => Refresh();
        private void Tint(Renderer renderer,Color color)
        {
            if (renderer == null) return;
            tint.Clear(); renderer.GetPropertyBlock(tint); tint.SetColor("_BaseColor",color); renderer.SetPropertyBlock(tint);
        }
        private GameObject Part(string name,PrimitiveType primitive,Transform parent,Vector3 localPosition,Vector3 scale,Color color,bool solid)
        {
            var go=GameObject.CreatePrimitive(primitive); go.name=name; go.transform.SetParent(parent,false);
            go.transform.localPosition=localPosition; go.transform.localScale=scale; go.layer=solid?8:0;
            var collider=go.GetComponent<Collider>(); collider.enabled=solid;
            if(!solid) Destroy(collider); // One-time graybox construction, never during combat.
            var renderer=go.GetComponent<Renderer>(); renderer.sharedMaterial=Resources.Load<Material>("M2/Stone");
            renderer.shadowCastingMode=ShadowCastingMode.On; Tint(renderer,color); return go;
        }
        private void OnDestroy()
        {
            if(Puzzle!=null) Puzzle.Changed-=Refresh;
            if(Challenge!=null) Challenge.Changed-=Refresh;
        }
    }
}
