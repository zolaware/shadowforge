/**
 * @file        Formats/GLTF/Exporter.cs
 * @brief       Exports HDB ModelFile to glTF 2.0 binary (.glb)
 *
 * @copyright   Copyright (c) 2026 Tom Clay <tomc@tctechstuff.com>
 *              All rights reserved.
 *
 * @license     BSD 3-Clause License
 *              See LICENSE file in the project root for full license text.
 */
using System.Numerics;
using ShadowForge.Formats.HDB;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace ShadowForge.Formats.GLTF;

using GltfMesh = MeshBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>;
using GltfVertex = VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>;

public static class Exporter
{
    private static readonly Vector4[] MaterialColors =
    [
        new(0.8f, 0.2f, 0.2f, 1f),
        new(0.2f, 0.6f, 0.8f, 1f),
        new(0.2f, 0.8f, 0.3f, 1f),
        new(0.8f, 0.7f, 0.2f, 1f),
        new(0.6f, 0.3f, 0.7f, 1f),
        new(0.8f, 0.5f, 0.2f, 1f),
        new(0.4f, 0.7f, 0.7f, 1f),
        new(0.7f, 0.4f, 0.5f, 1f),
    ];

    public static void Export(ModelFile model, string outputPath)
    {
        var scene = new SceneBuilder();
        Logger.Debug($"Check 1 (Inizio Export): Bones={model.Bones.Count}, MeshGroups={model.MeshGroups.Count}, VertexArrays={model.VertexArrays.Count}");
        // Compute global bone transforms (needed to transform vertices from bone-local to world space)
        var boneGlobals = ComputeBoneGlobals(model.Bones);

        // Build skeleton node hierarchy
        BuildSkeleton(model, scene);

        // Build materials (placeholder colors)
        var materials = BuildMaterials(model);

        // Build meshes with world-space vertex positions
        BuildMeshes(model, scene, materials, boneGlobals);

        var gltf = scene.ToGltf2();
        gltf.SaveGLB(outputPath);
    }

    // -----------------------------------------------------------------
    // Bone global transform computation
    // Matches Python's BD_Bone.get_global_matrix / get_global_pos exactly.
    // Uses 3x3 rotation matrices + 3D positions (not 4x4) to avoid
    // row-major/column-major convention issues.
    // -----------------------------------------------------------------

    private record BoneGlobal(float[,] Matrix, Vector3 Position);

    private static BoneGlobal[] ComputeBoneGlobals(List<Bone> bones)
    {
        var globals = new BoneGlobal[bones.Count];

        for (int i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];

            // local_matrix = (rot2 * rot3) * rot1
            var rot1 = EulerToMatrix3(bone.EulerX, bone.EulerY, bone.EulerZ);
            float[,] localMatrix;

            if (bone.ExtraEuler.Any(v => v != 0f))
            {
                var rot2 = EulerToMatrix3(bone.ExtraEuler[0], bone.ExtraEuler[1], bone.ExtraEuler[2]);
                var rot3 = EulerToMatrix3(bone.ExtraEuler[3], bone.ExtraEuler[4], bone.ExtraEuler[5]);
                localMatrix = Mat3Mul(Mat3Mul(rot2, rot3), rot1);
            }
            else
            {
                localMatrix = rot1;
            }

            var localPos = new Vector3(bone.PosX, bone.PosY, bone.PosZ);

            if (bone.ParentIndex < 0 || bone.ParentIndex >= bones.Count || globals[bone.ParentIndex] == null)
            {
                // Root bone
                globals[i] = new BoneGlobal(localMatrix, localPos);
            }
            else
            {
                var parent = globals[bone.ParentIndex];

                // global_matrix = (scale * parent_global) * local
                var scaleMatrix = new float[3, 3]
                {
                    { bone.ScaleX, 0, 0 },
                    { 0, bone.ScaleY, 0 },
                    { 0, 0, bone.ScaleZ },
                };
                var scaledParent = Mat3Mul(scaleMatrix, parent.Matrix);
                var globalMatrix = Mat3Mul(scaledParent, localMatrix);

                // global_pos = parent_global_matrix * local_pos + parent_global_pos
                var globalPos = Mat3Vec(parent.Matrix, localPos) + parent.Position;

                globals[i] = new BoneGlobal(globalMatrix, globalPos);
            }
        }

        return globals;
    }

    /// <summary>Euler XYZ to 3x3 rotation matrix. Order: Rz * Ry * Rx (matches Python).</summary>
    private static float[,] EulerToMatrix3(float x, float y, float z)
    {
        float cx = MathF.Cos(x), sx = MathF.Sin(x);
        float cy = MathF.Cos(y), sy = MathF.Sin(y);
        float cz = MathF.Cos(z), sz = MathF.Sin(z);

        return new float[3, 3]
        {
            { cy * cz, sx * sy * cz - cx * sz, cx * sy * cz + sx * sz },
            { cy * sz, sx * sy * sz + cx * cz, cx * sy * sz - sx * cz },
            { -sy,     sx * cy,                cx * cy                },
        };
    }

    private static float[,] Mat3Mul(float[,] a, float[,] b)
    {
        var r = new float[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                for (int k = 0; k < 3; k++)
                    r[i, j] += a[i, k] * b[k, j];
        return r;
    }

    private static Vector3 Mat3Vec(float[,] m, Vector3 v)
    {
        return new Vector3(
            m[0, 0] * v.X + m[0, 1] * v.Y + m[0, 2] * v.Z,
            m[1, 0] * v.X + m[1, 1] * v.Y + m[1, 2] * v.Z,
            m[2, 0] * v.X + m[2, 1] * v.Y + m[2, 2] * v.Z);
    }

    // -----------------------------------------------------------------
    // Skeleton
    // -----------------------------------------------------------------

    private static void BuildSkeleton(ModelFile model, SceneBuilder scene)
    {
        if (model.Bones.Count == 0) return;

        var nodes = new NodeBuilder[model.Bones.Count];
        for (int i = 0; i < model.Bones.Count; i++)
            nodes[i] = new NodeBuilder(model.Bones[i].Name);

        for (int i = 0; i < model.Bones.Count; i++)
        {
            var bone = model.Bones[i];
            var node = nodes[i];

            node.UseTranslation().Value = new Vector3(bone.PosX, bone.PosY, bone.PosZ);
            node.UseScale().Value = new Vector3(bone.ScaleX, bone.ScaleY, bone.ScaleZ);

            // Rotation as quaternion from the same euler convention
            var rot = EulerToMatrix3(bone.EulerX, bone.EulerY, bone.EulerZ);
            if (bone.ExtraEuler.Any(v => v != 0f))
            {
                var rot2 = EulerToMatrix3(bone.ExtraEuler[0], bone.ExtraEuler[1], bone.ExtraEuler[2]);
                var rot3 = EulerToMatrix3(bone.ExtraEuler[3], bone.ExtraEuler[4], bone.ExtraEuler[5]);
                rot = Mat3Mul(Mat3Mul(rot2, rot3), rot);
            }
            node.UseRotation().Value = Mat3ToQuaternion(rot);

            if (bone.ParentIndex >= 0 && bone.ParentIndex < nodes.Length)
                nodes[bone.ParentIndex].AddNode(node);
            else
                scene.AddNode(node);
        }
    }

    private static Quaternion Mat3ToQuaternion(float[,] m)
    {
        // Convert 3x3 rotation matrix to quaternion via System.Numerics
        var m4 = new Matrix4x4(
            m[0, 0], m[1, 0], m[2, 0], 0,
            m[0, 1], m[1, 1], m[2, 1], 0,
            m[0, 2], m[1, 2], m[2, 2], 0,
            0, 0, 0, 1);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m4));
    }

    // -----------------------------------------------------------------
    // Materials
    // -----------------------------------------------------------------

    private static MaterialBuilder[] BuildMaterials(ModelFile model)
    {
        int count = Math.Max(model.TextureCount, 1);
        var materials = new MaterialBuilder[count];
        for (int i = 0; i < count; i++)
        {
            var color = MaterialColors[i % MaterialColors.Length];
            string name = i < model.Textures.Count ? model.Textures[i].Name : $"material_{i}";
            materials[i] = new MaterialBuilder(name)
                .WithMetallicRoughnessShader()
                .WithBaseColor(color)
                .WithMetallicRoughness(0f, 0.8f);
        }
        return materials;
    }

    // -----------------------------------------------------------------
    // Mesh building with bone-local -> world-space vertex transform
    // -----------------------------------------------------------------

    private static void BuildMeshes(ModelFile model, SceneBuilder scene,
        MaterialBuilder[] materials, BoneGlobal[] boneGlobals)
    {
        var groupsByVa = model.MeshGroups
            .GroupBy(g => g.VaIndex)
            .ToList();

        foreach (var vaGroup in groupsByVa)
        {
            int vaIdx = vaGroup.Key;
            Logger.Debug($"Check 2 (Inizio vaGroup): vaIdx={vaIdx}. È valido? {vaIdx >= 0 && vaIdx < model.VertexArrays.Count}");
            if (vaIdx < 0 || vaIdx >= model.VertexArrays.Count) continue;

            var va = model.VertexArrays[vaIdx];
            // 1. Controlliamo cosa stiamo per passare al decoder
            int rawLength = va.RawVertices != null ? va.RawVertices.Length : -1;
            Logger.Debug($"Check 3A (Input Decoder): vaIdx={vaIdx}, VertexCount attesi={va.VertexCount}, VaType=0x{va.VaType:X2}, Byte RawVertices={rawLength}");

            // 2. Chiamata al decoder originale
            var rawVertices = VertexDecoder.Decode(va.RawVertices, va.VaType, va.VertexCount);

            // 3. Risultato
            Logger.Debug($"Check 3B (Output Decoder): Per vaIdx={vaIdx}, Vertici decodificati={rawVertices.Count}");

            if (rawVertices.Count == 0) continue;

            var mesh = new GltfMesh($"mesh_{vaIdx}");

            foreach (var group in vaGroup)
            {
                if (group.IaIndex < 0 || group.IaIndex >= model.IndexArrays.Count) continue;
                var ia = model.IndexArrays[group.IaIndex];
                Logger.Debug($"Check 4 (Elaborazione Gruppo): iaIndex={group.IaIndex}, Numero Indici={ia.Indices.Length}, Topologia=0x{group.Topology:X2}");
                // Transform vertices from bone-local to world space using this group's bone palette
                var worldVertices = TransformVertices(rawVertices, group.BonePalette, boneGlobals);

                int matIdx = Math.Clamp(group.MaterialIndex, 0, materials.Length - 1);
                var prim = mesh.UsePrimitive(materials[matIdx]);

                bool isStrip = group.Topology == 0x20 || group.Topology == 0x30;
                if (isStrip)
                    AddTriangleStrip(prim, worldVertices, ia.Indices);
                else
                    AddTriangleList(prim, worldVertices, ia.Indices);
            }

            scene.AddRigidMesh(mesh, Matrix4x4.Identity);
        }
    }

    /// <summary>
    /// Transforms vertices from bone-local space to world space, blending all
    /// bone influences matching the Python parser's skinning formula.
    /// </summary>
    private static List<GltfVertex> TransformVertices(
        List<Vertex> vertices, List<ushort> palette, BoneGlobal[] boneGlobals)
    {
        var result = new List<GltfVertex>(vertices.Count);
        foreach (var v in vertices)
        {
            var worldPos = Vector3.Zero;
            float totalWeight = 0f;

            foreach (var inf in v.Influences)
            {
                int palIdx = inf.PaletteIndex;
                int boneIdx = palIdx < palette.Count ? palette[palIdx] : 0;
                if (boneIdx < 0 || boneIdx >= boneGlobals.Length || boneGlobals[boneIdx] == null)
                    continue;

                var bg = boneGlobals[boneIdx];
                var localPos = new Vector3(inf.PosX, inf.PosY, inf.PosZ);
                var transformed = Mat3Vec(bg.Matrix, localPos) + bg.Position;

                float w = inf.Weight;
                worldPos += w * transformed;
                totalWeight += w;
            }

            // Give remaining weight to first influence (Python's blended_weight)
            if (v.Influences.Count > 0 && totalWeight < 1f)
            {
                var inf0 = v.Influences[0];
                int boneIdx0 = inf0.PaletteIndex < palette.Count ? palette[inf0.PaletteIndex] : 0;
                if (boneIdx0 >= 0 && boneIdx0 < boneGlobals.Length && boneGlobals[boneIdx0] != null)
                {
                    var bg0 = boneGlobals[boneIdx0];
                    var localPos0 = new Vector3(inf0.PosX, inf0.PosY, inf0.PosZ);
                    var transformed0 = Mat3Vec(bg0.Matrix, localPos0) + bg0.Position;
                    worldPos += (1f - totalWeight) * transformed0;
                }
            }

            // Transform normal by primary bone's rotation
            Vector3 worldNormal;
            if (v.Influences.Count > 0)
            {
                int palIdx = v.Influences[0].PaletteIndex;
                int boneIdx = palIdx < palette.Count ? palette[palIdx] : 0;
                if (boneIdx >= 0 && boneIdx < boneGlobals.Length && boneGlobals[boneIdx] != null)
                {
                    var localNorm = new Vector3(v.NormalX / 32767f, v.NormalY / 32767f, v.NormalZ / 32767f);
                    worldNormal = Vector3.Normalize(Mat3Vec(boneGlobals[boneIdx].Matrix, localNorm));
                }
                else
                {
                    worldNormal = Vector3.Normalize(new Vector3(v.NormalX / 32767f, v.NormalY / 32767f, v.NormalZ / 32767f));
                }
            }
            else
            {
                worldNormal = Vector3.Normalize(new Vector3(v.NormalX / 32767f, v.NormalY / 32767f, v.NormalZ / 32767f));
            }

            if (float.IsNaN(worldNormal.X)) worldNormal = Vector3.UnitY;

            var uv = new Vector2(v.U, v.V);
            result.Add(new GltfVertex(
                new VertexPositionNormal(worldPos, worldNormal),
                new VertexTexture1(uv)));
        }
        return result;
    }

    // -----------------------------------------------------------------
    // Triangle assembly
    // -----------------------------------------------------------------

    private static void AddTriangleList(IPrimitiveBuilder prim, List<GltfVertex> vertices, ushort[] indices)
    {
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            if (indices[i] >= vertices.Count || indices[i + 1] >= vertices.Count || indices[i + 2] >= vertices.Count)
            {
                Logger.Debug($"Check 5A (Scarto TriangleList): Indici fuori limite! i={i}, v.Count={vertices.Count}, idx0={indices[i]}, idx1={indices[i + 1]}, idx2={indices[i + 2]}");
                continue;
            }
            prim.AddTriangle(vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]]);
        }
    }

    private static void AddTriangleStrip(IPrimitiveBuilder prim, List<GltfVertex> vertices, ushort[] indices)
    {
        int stripStart = 0;
        for (int i = 0; i <= indices.Length; i++)
        {
            if (i == indices.Length || indices[i] == 0xFFFF)
            {
                EmitStrip(prim, vertices, indices, stripStart, i);
                stripStart = i + 1;
            }
        }
    }

    private static void EmitStrip(IPrimitiveBuilder prim, List<GltfVertex> vertices,
        ushort[] indices, int start, int end)
    {
        int count = end - start;
        if (count < 3) return;

        for (int i = 0; i < count - 2; i++)
        {
            int vi0 = indices[start + i];
            int vi1 = indices[start + i + 1];
            int vi2 = indices[start + i + 2];

            if (vi0 == vi1 || vi1 == vi2 || vi0 == vi2) continue;
            if (vi0 >= vertices.Count || vi1 >= vertices.Count || vi2 >= vertices.Count)
            {
                // INCOLLA QUI:
                Logger.Debug($"Check 5B (Scarto Strip): Indici fuori limite! v.Count={vertices.Count}, vi0={vi0}, vi1={vi1}, vi2={vi2}");
                continue;
            }

            if (i % 2 == 0)
                prim.AddTriangle(vertices[vi0], vertices[vi1], vertices[vi2]);
            else
                prim.AddTriangle(vertices[vi1], vertices[vi0], vertices[vi2]);
        }
    }
}
