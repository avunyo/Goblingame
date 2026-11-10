using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Goblingame.TestScene.Editor
{
    /// <summary>
    /// Mesh checks / repairs for the goblin: finds holes (open boundary loops) and can patch them by
    /// writing a repaired copy of the mesh as an asset (the .blend source is never re-imported).
    /// </summary>
    [InitializeOnLoad]
    public static class GoblinMeshTools
    {
        private static string Root => Directory.GetParent(Application.dataPath).FullName;
        private static string Request => Path.Combine(Root, "Temp/GoblinMesh.request");

        static GoblinMeshTools()
        {
            if (File.Exists(Request)) EditorApplication.delayCall += RunQueued;
        }

        internal static void RunQueued()
        {
            if (!File.Exists(Request)) return;
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.delayCall += RunQueued; return; }
            string cmd = File.ReadAllText(Request).Trim(); File.Delete(Request);
            if (cmd == "diagnose") Diagnose();
            if (cmd == "fix") FixGoblin();
            GoblinPlaytestLauncher.StartIfRequested();
        }

        public struct Loop { public List<int> verts; public Vector3 centroid; public float size; }

        /// <summary>Open boundary loops of a mesh, with vertices welded by position (UV seams are not holes).</summary>
        public static List<Loop> FindHoles(Mesh mesh, out int[] weld)
        {
            var v = mesh.vertices; weld = new int[v.Length];
            var map = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < v.Length; i++)
            {
                var key = Vector3Int.RoundToInt(v[i] * 10000f);
                if (!map.TryGetValue(key, out int w)) { w = i; map[key] = i; }
                weld[i] = w;
            }
            var edges = new Dictionary<(int, int), int>();   // directed edge → count
            var t = mesh.triangles;
            for (int i = 0; i < t.Length; i += 3)
                for (int k = 0; k < 3; k++)
                {
                    int a = weld[t[i + k]], b = weld[t[i + (k + 1) % 3]];
                    if (a == b) continue;
                    edges.TryGetValue((a, b), out int c); edges[(a, b)] = c + 1;
                }
            // a boundary edge has no twin in the opposite direction
            var next = new Dictionary<int, int>();
            foreach (var e in edges.Keys)
                if (!edges.ContainsKey((e.Item2, e.Item1))) next[e.Item2] = e.Item1;   // reversed: walk the hole's rim so the patch faces outward
            var loops = new List<Loop>(); var used = new HashSet<int>();
            foreach (int start in next.Keys)
            {
                if (used.Contains(start)) continue;
                var loop = new List<int>(); int cur = start; int guard = 0;
                while (!used.Contains(cur) && next.ContainsKey(cur) && guard++ < 10000) { used.Add(cur); loop.Add(cur); cur = next[cur]; }
                if (loop.Count < 3) continue;
                Vector3 c = Vector3.zero; foreach (int i in loop) c += v[i]; c /= loop.Count;
                float size = loop.Max(i => (v[i] - c).magnitude);
                loops.Add(new Loop { verts = loop, centroid = c, size = size });
            }
            return loops;
        }

        public static Vector3 TriCenter(Mesh m, int tri) { var v = m.vertices; var t = m.triangles; return (v[t[tri * 3]] + v[t[tri * 3 + 1]] + v[t[tri * 3 + 2]]) / 3f; }

        /// <summary>Triangles whose stored vertex normals point against the triangle's own facing (rendered dark / "missing").</summary>
        public static List<int> InvertedTriangles(Mesh m)
        {
            var v = m.vertices; var n = m.normals; var t = m.triangles; var bad = new List<int>();
            if (n == null || n.Length != v.Length) return bad;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 face = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                if (face.sqrMagnitude < 1e-14f) continue;
                Vector3 avg = n[t[i]] + n[t[i + 1]] + n[t[i + 2]];
                if (Vector3.Dot(face.normalized, avg.normalized) < -0.2f) bad.Add(i / 3);
            }
            return bad;
        }

        /// <summary>Readable copy of any mesh (works for non-readable imports inside the Editor via MeshData).</summary>
        public static Mesh ReadableCopy(Mesh src)
        {
            if (src.isReadable) return Object.Instantiate(src);
            using (var data = Mesh.AcquireReadOnlyMeshData(src))
            {
                var dst = new Mesh { name = src.name + " (fixed)" };
                var write = Mesh.AllocateWritableMeshData(1);
                var md = data[0]; var wd = write[0];
                var attrs = src.GetVertexAttributes();
                wd.SetVertexBufferParams(md.vertexCount, attrs);
                for (int stream = 0; stream < md.vertexBufferCount; stream++)
                {
                    var from = md.GetVertexData<byte>(stream); var to = wd.GetVertexData<byte>(stream);
                    Unity.Collections.NativeArray<byte>.Copy(from, to, from.Length);
                }
                bool wide = md.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32;
                int indexCount = 0; for (int i = 0; i < md.subMeshCount; i++) indexCount += md.GetSubMesh(i).indexCount;
                wd.SetIndexBufferParams(indexCount, md.indexFormat);
                if (wide) { var a = md.GetIndexData<uint>(); var b = wd.GetIndexData<uint>(); Unity.Collections.NativeArray<uint>.Copy(a, b, a.Length); }
                else { var a = md.GetIndexData<ushort>(); var b = wd.GetIndexData<ushort>(); Unity.Collections.NativeArray<ushort>.Copy(a, b, a.Length); }
                wd.subMeshCount = md.subMeshCount;
                for (int i = 0; i < md.subMeshCount; i++) wd.SetSubMesh(i, md.GetSubMesh(i), UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds);
                Mesh.ApplyAndDisposeWritableMeshData(write, dst);
                dst.bindposes = src.bindposes;
                dst.bounds = src.bounds;
                for (int i = 0; i < src.blendShapeCount; i++) Debug.LogWarning("[GoblinMesh] blend shape " + src.GetBlendShapeName(i) + " is not copied");
                return dst;
            }
        }

        private const string FixedMeshPath = "Assets/RecoveredGoblinGraphics/Generated/GoblinBody_Fixed.asset";

        /// <summary>
        /// Patches the holes (e.g. the missing polygon on the nose) and inverted normals of the goblin body,
        /// saves the result as a mesh asset and puts it on the scene goblin. The .blend source is untouched.
        /// </summary>
        [MenuItem("Goblingame/Fix Goblin Mesh (nose hole)")]
        public static void FixGoblin()
        {
            var scene = SceneManager.GetActiveScene();
            var player = scene.GetRootGameObjects().FirstOrDefault(o => o.name == "Player");
            var smr = player != null ? player.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name == "Cube") : null;
            if (smr == null || smr.sharedMesh == null) { Debug.LogWarning("[GoblinMesh] goblin body (Cube) not found"); return; }
            Mesh source = smr.sharedMesh;
            if (AssetDatabase.GetAssetPath(source) == FixedMeshPath)
            {
                // always rebuild from the original imported body
                source = AssetDatabase.LoadAllAssetsAtPath("Assets/RecoveredGoblinGraphics/BlenderSource/гоблин с высоким лицом.blend")
                    .OfType<Mesh>().FirstOrDefault(m => m.name == "Cube");
                if (source == null) { Debug.LogWarning("[GoblinMesh] original body mesh not found"); return; }
            }

            var mesh = ReadableCopy(source);
            mesh.name = "GoblinBody_Fixed";
            var verts = new List<Vector3>(mesh.vertices); var normals = new List<Vector3>(mesh.normals);
            var uv = new List<Vector2>(); mesh.GetUVs(0, uv);
            var tangents = new List<Vector4>(mesh.tangents); var colors = new List<Color>(mesh.colors);
            var weights = new List<BoneWeight>(mesh.boneWeights);

            // 1) inverted normals → give those triangles their own vertices with the triangle's real facing
            int flipped = 0;
            var subTris0 = new List<int>(mesh.GetTriangles(0));
            foreach (int tri in InvertedTriangles(mesh))
            {
                if (tri * 3 + 2 >= subTris0.Count) continue;
                Vector3 a = verts[subTris0[tri * 3]], b = verts[subTris0[tri * 3 + 1]], c3 = verts[subTris0[tri * 3 + 2]];
                Vector3 fn = Vector3.Cross(b - a, c3 - a).normalized;
                for (int k = 0; k < 3; k++)
                {
                    int i = subTris0[tri * 3 + k];
                    verts.Add(verts[i]); normals.Add(fn); uv.Add(uv[i]);
                    if (tangents.Count > 0) tangents.Add(tangents[i]);
                    if (colors.Count > 0) colors.Add(colors[i]);
                    if (weights.Count > 0) weights.Add(weights[i]);
                    subTris0[tri * 3 + k] = verts.Count - 1;
                }
                flipped += 3;
            }

            // 2) holes → fan patch on duplicated rim vertices (own flat normal, same UVs / bone weights)
            var loops = FindHoles(mesh, out _);
            var subTris = subTris0;
            int patched = 0;
            foreach (var loop in loops)
            {
                if (loop.verts.Count > 12) continue;   // only small holes, never open edges of the design
                Vector3 c = loop.centroid;
                Vector3 avgN = Vector3.zero; foreach (int i in loop.verts) avgN += normals[i];
                Vector3 faceN = Vector3.zero;
                for (int k = 0; k < loop.verts.Count; k++)
                    faceN += Vector3.Cross(verts[loop.verts[k]] - c, verts[loop.verts[(k + 1) % loop.verts.Count]] - c);
                bool reverse = Vector3.Dot(faceN, avgN) < 0f;
                Vector3 n = (reverse ? -faceN : faceN).normalized;
                int baseIndex = verts.Count;
                foreach (int i in loop.verts)
                {
                    verts.Add(verts[i]); normals.Add(n); uv.Add(uv.Count > i ? uv[i] : Vector2.zero);
                    if (tangents.Count > 0) tangents.Add(tangents[i]);
                    if (colors.Count > 0) colors.Add(colors[i]);
                    if (weights.Count > 0) weights.Add(weights[i]);
                }
                // centre vertex: averaged UV, weights of the nearest rim vertex
                int nearest = loop.verts.OrderBy(i => (verts[i] - c).sqrMagnitude).First();
                Vector2 cuv = Vector2.zero; foreach (int i in loop.verts) cuv += uv[i]; cuv /= loop.verts.Count;
                verts.Add(c); normals.Add(n); uv.Add(cuv);
                if (tangents.Count > 0) tangents.Add(tangents[nearest]);
                if (colors.Count > 0) colors.Add(colors[nearest]);
                if (weights.Count > 0) weights.Add(weights[nearest]);
                int center = verts.Count - 1;
                for (int k = 0; k < loop.verts.Count; k++)
                {
                    int a = baseIndex + k, b = baseIndex + (k + 1) % loop.verts.Count;
                    if (reverse) subTris.AddRange(new[] { center, b, a }); else subTris.AddRange(new[] { center, a, b });
                }
                patched++;
                Debug.Log("[GoblinMesh] patched a " + loop.verts.Count + "-edge hole at " + smr.transform.TransformPoint(c).ToString("0.000"));
            }

            mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : mesh.indexFormat;
            var otherSubs = Enumerable.Range(1, mesh.subMeshCount - 1).Select(i => mesh.GetTriangles(i)).ToList();
            mesh.SetVertices(verts); mesh.SetNormals(normals); mesh.SetUVs(0, uv);
            if (tangents.Count == verts.Count) mesh.SetTangents(tangents);
            if (colors.Count == verts.Count) mesh.SetColors(colors);
            if (weights.Count == verts.Count) mesh.boneWeights = weights.ToArray();
            mesh.SetTriangles(subTris, 0);
            for (int i = 0; i < otherSubs.Count; i++) mesh.SetTriangles(otherSubs[i], i + 1);
            mesh.bindposes = source.bindposes;
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);

            AssetDatabase.DeleteAsset(FixedMeshPath);
            AssetDatabase.CreateAsset(mesh, FixedMeshPath);
            Undo.RecordObject(smr, "Fixed goblin mesh");
            smr.sharedMesh = mesh;
            PrefabUtility.RecordPrefabInstancePropertyModifications(smr);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            var check = FindHoles(mesh, out _);
            Debug.Log("[GoblinMesh] FIXED: " + patched + " holes patched, " + flipped / 3 + " triangles' normals turned; remaining holes " + check.Count +
                      ", inverted " + InvertedTriangles(mesh).Count + " → " + FixedMeshPath);
        }

        [MenuItem("Goblingame/Diagnose Goblin Mesh")]
        public static void Diagnose()
        {
            var player = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(o => o.name == "Player");
            if (player == null) { Debug.LogWarning("[GoblinMesh] no Player"); return; }
            var rs = player.GetComponentsInChildren<Renderer>(true);
            Bounds all = rs[0].bounds; foreach (var r in rs) all.Encapsulate(r.bounds);
            Debug.Log("[GoblinMesh] " + rs.Length + " renderers, height " + all.size.y.ToString("0.00"));
            foreach (var r in rs.Where(x => !x.name.StartsWith("WGT")))
            {
                Mesh mm = r is SkinnedMeshRenderer s2 ? s2.sharedMesh : r.GetComponent<MeshFilter>() != null ? r.GetComponent<MeshFilter>().sharedMesh : null;
                Debug.Log("[GoblinMesh] renderer " + r.name + " type=" + r.GetType().Name + " enabled=" + r.enabled + " active=" + r.gameObject.activeInHierarchy +
                          " mesh=" + (mm != null ? mm.name + " readable=" + mm.isReadable + " v=" + mm.vertexCount + " path=" + AssetDatabase.GetAssetPath(mm) : "none") +
                          " mats=" + string.Join(",", r.sharedMaterials.Select(x => x != null ? x.name + "/" + x.shader.name : "null")));
            }
            foreach (var r in rs)
            {
                Mesh m = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>() != null ? r.GetComponent<MeshFilter>().sharedMesh : null;
                if (m == null || r.name.StartsWith("WGT")) continue;
                if (!m.isReadable)
                {
                    try { m = ReadableCopy(m); Debug.Log("[GoblinMesh] " + r.name + ": read a copy of non-readable mesh (" + m.vertexCount + "v, weights=" + (m.boneWeights.Length > 0) + ")"); }
                    catch (System.Exception e) { Debug.Log("[GoblinMesh] " + r.name + " mesh not readable: " + e.Message); continue; }
                }
                var loops = FindHoles(m, out _);
                if (r.enabled && r.gameObject.activeInHierarchy && !r.name.StartsWith("WGT"))
                {
                    var bad = InvertedTriangles(m);
                    Debug.Log("[GoblinMesh] visible " + r.name + " (" + m.name + ", " + m.vertexCount + "v, " + m.triangles.Length / 3 + "t, readable) holes=" + loops.Count +
                              " inverted-normal triangles=" + bad.Count + (bad.Count > 0 ? " e.g. at " + string.Join(" ", bad.Take(6).Select(i => r.transform.TransformPoint(TriCenter(m, i)).ToString("0.000"))) : ""));
                }
                Vector3 c = r.bounds.center;
                string pos = "h=" + ((c.y - all.min.y) / all.size.y).ToString("0.00") + " fwd=" + Vector3.Dot(c - all.center, player.transform.forward).ToString("0.00");
                if (loops.Count > 0 || rs.Length < 12)
                    Debug.Log("[GoblinMesh] " + r.name + " (" + m.name + ", " + m.vertexCount + "v, " + m.triangles.Length / 3 + "t, " + pos + ") holes: " +
                              string.Join("; ", loops.Select(l => l.verts.Count + " edges @" + r.transform.TransformPoint(l.centroid).ToString("0.000") + " size " + (l.size * r.transform.lossyScale.x).ToString("0.000"))));
            }
        }
    }
}
