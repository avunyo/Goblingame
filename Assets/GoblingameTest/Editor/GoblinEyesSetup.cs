using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Goblingame.TestScene.Editor
{
    public static class GoblinEyesSetup
    {
        [MenuItem("Goblingame/Add Relaxed Eye Movement")]
        public static void Apply()
        {
            var scene=SceneManager.GetActiveScene();
            if(EditorApplication.isPlaying||scene.path!="Assets/Scenes/GoblinPlayable.unity")throw new InvalidOperationException("Open GoblinPlayable outside Play Mode.");
            var player=scene.GetRootGameObjects().First(o=>o.name=="Player");
            var face=player.GetComponentsInChildren<SkinnedMeshRenderer>().OrderByDescending(r=>r.sharedMesh.vertexCount).First();
            var source=face.sharedMesh;var uv=source.uv;
            int affected=uv.Count(p=>GoblinIdleEyes.PupilWeight(p)>.01f);
            if(affected<10||affected>uv.Length/4)throw new InvalidOperationException("Unexpected eye UV mask: "+affected);
            if(!EditorSceneManager.SaveScene(scene,AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/GoblinBeforeEyes.unity"),true))throw new InvalidOperationException("Backup failed.");
            // A separate readable mesh lets each character animate its own UVs without
            // touching the FBX, texture, shared materials, skeleton or other instances.
            var mesh=new Mesh{name="Goblin Eyes Mesh",indexFormat=source.indexFormat};
            mesh.vertices=source.vertices;mesh.normals=source.normals;mesh.tangents=source.tangents;mesh.colors=source.colors;
            for(int channel=0;channel<8;channel++){var values=new System.Collections.Generic.List<Vector4>();source.GetUVs(channel,values);if(values.Count>0)mesh.SetUVs(channel,values);}
            mesh.bindposes=source.bindposes;mesh.boneWeights=source.boneWeights;mesh.subMeshCount=source.subMeshCount;
            for(int i=0;i<source.subMeshCount;i++)mesh.SetIndices(source.GetIndices(i),source.GetTopology(i),i,false);
            for(int shape=0;shape<source.blendShapeCount;shape++)for(int frame=0;frame<source.GetBlendShapeFrameCount(shape);frame++)
            {
                var v=new Vector3[source.vertexCount];var n=new Vector3[v.Length];var t=new Vector3[v.Length];source.GetBlendShapeFrameVertices(shape,frame,v,n,t);
                mesh.AddBlendShapeFrame(source.GetBlendShapeName(shape),source.GetBlendShapeFrameWeight(shape,frame),v,n,t);
            }
            mesh.bounds=source.bounds;
            string path=AssetDatabase.GenerateUniqueAssetPath("Assets/GoblingameTest/GoblinEyes.asset");AssetDatabase.CreateAsset(mesh,path);
            face.sharedMesh=mesh;PrefabUtility.RecordPrefabInstancePropertyModifications(face);
            var eyes=player.GetComponent<GoblinIdleEyes>()??player.AddComponent<GoblinIdleEyes>();
            var data=new SerializedObject(eyes);data.FindProperty("face").objectReferenceValue=face;data.ApplyModifiedProperties();
            if(!mesh.isReadable||!mesh.vertices.SequenceEqual(source.vertices)||!mesh.uv.SequenceEqual(uv))throw new InvalidOperationException("Mesh copy validation failed.");
            EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
            Debug.Log("Relaxed eye movement saved; pupil mask vertices="+affected+"; geometry, skin weights and original texture preserved.");
        }
    }
}
