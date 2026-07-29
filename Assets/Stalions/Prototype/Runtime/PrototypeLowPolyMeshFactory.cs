using System.Collections.Generic;
using UnityEngine;

namespace Stalions.Prototype
{
    internal static class PrototypeLowPolyMeshFactory
    {
        private const int SideCount = 7;

        private static Mesh cone;
        private static Mesh trunk;
        private static readonly Mesh[] Rocks = new Mesh[3];

        public static Mesh Cone => cone ??= CreateCone();
        public static Mesh Trunk => trunk ??= CreateTrunk();

        public static Mesh Rock(int variant)
        {
            var index = Mathf.Abs(variant) % Rocks.Length;
            return Rocks[index] ??= CreateRock(index);
        }

        private static Mesh CreateCone()
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (var side = 0; side < SideCount; side++)
            {
                var first = RingPoint(side, SideCount, 1f, 0f);
                var second = RingPoint(side + 1, SideCount, 1f, 0f);
                AddTriangle(vertices, triangles, first, new Vector3(0f, 1f, 0f), second);
                AddTriangle(vertices, triangles, Vector3.zero, first, second);
            }

            return BuildMesh("Prototype Low Poly Cone", vertices, triangles);
        }

        private static Mesh CreateTrunk()
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (var side = 0; side < SideCount; side++)
            {
                var bottomFirst = RingPoint(side, SideCount, 1f, 0f);
                var bottomSecond = RingPoint(side + 1, SideCount, 1f, 0f);
                var topFirst = RingPoint(side, SideCount, 0.72f, 1f);
                var topSecond = RingPoint(side + 1, SideCount, 0.72f, 1f);
                AddQuad(
                    vertices,
                    triangles,
                    bottomFirst,
                    bottomSecond,
                    topSecond,
                    topFirst);
                AddTriangle(vertices, triangles, Vector3.zero, bottomFirst, bottomSecond);
                AddTriangle(vertices, triangles, Vector3.up, topSecond, topFirst);
            }

            return BuildMesh("Prototype Low Poly Trunk", vertices, triangles);
        }

        private static Mesh CreateRock(int variant)
        {
            var sides = 6 + variant;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var rotationOffset = variant * 0.21f;
            var apex = new Vector3(
                -0.12f + variant * 0.11f,
                1f,
                0.08f - variant * 0.09f);

            for (var side = 0; side < sides; side++)
            {
                var firstRadius = 0.82f + 0.13f * Mathf.Sin(side * 2.17f + variant);
                var secondRadius = 0.82f + 0.13f * Mathf.Sin((side + 1) * 2.17f + variant);
                var lowerFirst = RingPoint(side, sides, firstRadius, 0f, rotationOffset);
                var lowerSecond = RingPoint(side + 1, sides, secondRadius, 0f, rotationOffset);
                var upperFirst = RingPoint(side, sides, firstRadius * 0.68f, 0.55f, rotationOffset + 0.12f);
                var upperSecond = RingPoint(
                    side + 1,
                    sides,
                    secondRadius * 0.68f,
                    0.55f,
                    rotationOffset + 0.12f);

                AddQuad(
                    vertices,
                    triangles,
                    lowerFirst,
                    lowerSecond,
                    upperSecond,
                    upperFirst);
                AddTriangle(vertices, triangles, upperFirst, apex, upperSecond);
                AddTriangle(vertices, triangles, Vector3.zero, lowerFirst, lowerSecond);
            }

            return BuildMesh($"Prototype Low Poly Rock {variant}", vertices, triangles);
        }

        private static Vector3 RingPoint(
            int index,
            int count,
            float radius,
            float height,
            float angleOffset = 0f)
        {
            var angle = index * Mathf.PI * 2f / count + angleOffset;
            return new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius);
        }

        private static void AddTriangle(
            List<Vector3> vertices,
            List<int> triangles,
            Vector3 first,
            Vector3 second,
            Vector3 third)
        {
            var start = vertices.Count;
            vertices.Add(first);
            vertices.Add(second);
            vertices.Add(third);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
        }

        private static void AddQuad(
            List<Vector3> vertices,
            List<int> triangles,
            Vector3 bottomFirst,
            Vector3 bottomSecond,
            Vector3 topSecond,
            Vector3 topFirst)
        {
            var start = vertices.Count;
            vertices.Add(bottomFirst);
            vertices.Add(bottomSecond);
            vertices.Add(topSecond);
            vertices.Add(topFirst);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 1);
            triangles.Add(start);
            triangles.Add(start + 3);
            triangles.Add(start + 2);
        }

        private static Mesh BuildMesh(
            string meshName,
            List<Vector3> vertices,
            List<int> triangles)
        {
            var mesh = new Mesh
            {
                name = meshName
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
