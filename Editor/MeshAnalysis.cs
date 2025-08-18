// MeshAnalysis.cs

using System;
using System.Collections.Generic;
using UnityEngine;

public static class MeshAnalysis
{
    /// <summary>
    // �e�|���S�����ǂ̃T�u���b�V���ɑ����邩���}�b�s���O�����z����\�z���܂��B
    /// </summary>
    /// <param name="mesh">��͑Ώۂ̃��b�V���B</param>
    /// <returns>�|���S���̃C���f�b�N�X���L�[�Ƃ���T�u���b�V���C���f�b�N�X�̔z��B</returns>
    public static int[] BuildTriangleToSubmeshMap(Mesh mesh)
    {
        if (mesh == null) return null;

        int triCount = mesh.triangles.Length / 3;
        var map = new int[triCount];
        int globalIdx = 0;
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            var tris = mesh.GetTriangles(s);
            for (int i = 0; i < tris.Length; i += 3)
            {
                if (globalIdx < map.Length) map[globalIdx] = s;
                globalIdx++;
            }
        }
        return map;
    }

    /// <summary>
    /// 3D���f����ł̃|���S���̗אڊ֌W�}�b�v���\�z���܂��B
    /// </summary>
    /// <param name="mesh">��͑Ώۂ̃��b�V���B</param>
    /// <returns>�|���S���̃C���f�b�N�X���L�[�Ƃ���A�אڃ|���S�����X�g�̎����B</returns>
    public static Dictionary<int, List<int>> BuildAdjacencyMap(Mesh mesh)
    {
        var map = new Dictionary<int, List<int>>();
        if (mesh == null) return map;

        int[] triangles = mesh.triangles;
        int triangleCount = triangles.Length / 3;
        var edgeMap = new Dictionary<long, List<int>>();

        for (int i = 0; i < triangleCount; i++)
        {
            int triBase = i * 3;
            int v0 = triangles[triBase], v1 = triangles[triBase + 1], v2 = triangles[triBase + 2];
            Action<int, int> AddEdge = (tv0, tv1) =>
            {
                long key = GetEdgeKey(tv0, tv1);
                if (!edgeMap.ContainsKey(key)) edgeMap[key] = new List<int>();
                edgeMap[key].Add(i);
            };
            AddEdge(v0, v1); AddEdge(v1, v2); AddEdge(v2, v0);
        }

        for (int i = 0; i < triangleCount; i++) map[i] = new List<int>();

        foreach (var edgeTriangles in edgeMap.Values)
        {
            if (edgeTriangles.Count == 2)
            {
                int t0 = edgeTriangles[0], t1 = edgeTriangles[1];
                if (!map[t0].Contains(t1)) map[t0].Add(t1);
                if (!map[t1].Contains(t0)) map[t1].Add(t1);
            }
        }
        return map;
    }

    /// <summary>
    /// UV���W��ł̃|���S���̗אڊ֌W�}�b�v���\�z���܂��B
    /// </summary>
    /// <param name="mesh">��͑Ώۂ̃��b�V���B</param>
    /// <returns>�|���S���̃C���f�b�N�X���L�[�Ƃ���AUV��ŗאڂ���|���S�����X�g�̎����B</returns>
    public static Dictionary<int, List<int>> BuildUVAdjacencyMap(Mesh mesh)
    {
        var map = new Dictionary<int, List<int>>();
        if (mesh == null || mesh.uv.Length == 0) return map;

        int[] triangles = mesh.triangles;
        Vector2[] uvs = mesh.uv;
        int triangleCount = triangles.Length / 3;
        var edgeToTriangles = new Dictionary<UvEdge, List<int>>();

        for (int i = 0; i < triangleCount; i++)
        {
            int i0 = triangles[i * 3 + 0], i1 = triangles[i * 3 + 1], i2 = triangles[i * 3 + 2];
            Vector2 uv0 = uvs[i0], uv1 = uvs[i1], uv2 = uvs[i2];

            Action<Vector2, Vector2> AddEdge = (u, v) =>
            {
                var edge = new UvEdge(u, v);
                if (!edgeToTriangles.ContainsKey(edge)) edgeToTriangles[edge] = new List<int>();
                edgeToTriangles[edge].Add(i);
            };
            AddEdge(uv0, uv1); AddEdge(uv1, uv2); AddEdge(uv2, uv0);
        }

        for (int i = 0; i < triangleCount; i++) map[i] = new List<int>();

        foreach (var pair in edgeToTriangles)
        {
            if (pair.Value.Count > 1)
            {
                for (int i = 0; i < pair.Value.Count; i++)
                    for (int j = i + 1; j < pair.Value.Count; j++)
                    {
                        int t1 = pair.Value[i], t2 = pair.Value[j];
                        if (!map[t1].Contains(t2)) map[t1].Add(t2);
                        if (!map[t2].Contains(t1)) map[t2].Add(t1);
                    }
            }
        }
        return map;
    }

    /// <summary>
    /// 3D��Ԃŗאڂ��A����UV���A�����Ă���|���S���̗אڃ}�b�v���\�z���܂��B
    /// </summary>
    /// <param name="mesh">��͑Ώۂ̃��b�V���B</param>
    /// <param name="fullAdjacencyMap">3D��Ԃł̊��S�ȗאڃ}�b�v�B</param>
    /// <param name="uvAdjacencyMap">UV��Ԃł̗אڃ}�b�v�B</param>
    /// <returns>UV�̐؂�ڂ��l�������אڃ|���S�����X�g�̎����B</returns>
    public static Dictionary<int, List<int>> BuildUVContiguousAdjacencyMap(Mesh mesh, Dictionary<int, List<int>> fullAdjacencyMap, Dictionary<int, List<int>> uvAdjacencyMap)
    {
        var map = new Dictionary<int, List<int>>();
        if (mesh == null || fullAdjacencyMap == null || uvAdjacencyMap == null) return map;

        int triCount = mesh.triangles.Length / 3;
        for (int i = 0; i < triCount; i++)
        {
            map[i] = new List<int>();
            if (fullAdjacencyMap.TryGetValue(i, out var neighbors) && uvAdjacencyMap.TryGetValue(i, out var uvNeighbors))
            {
                var uvNeighborSet = new HashSet<int>(uvNeighbors);
                foreach (var neighbor in neighbors)
                {
                    if (uvNeighborSet.Contains(neighbor))
                    {
                        map[i].Add(neighbor);
                    }
                }
            }
        }
        return map;
    }


    /// <summary>
    /// �����ȃ{�b�N�X�u���[��K�p���܂��B
    /// </summary>
    /// <param name="pixels">�����Ώۂ̃s�N�Z���z��B</param>
    /// <param name="w">�摜�̕��B</param>
    /// <param name="h">�摜�̍����B</param>
    /// <param name="radius">�ڂ����̔��a�B</param>
    /// <param name="iterations">�����񐔁B</param>
    public static void ApplyFastBlur(Color32[] pixels, int w, int h, int radius, int iterations)
    {
        var tempPixels = new Color32[pixels.Length];
        for (var i = 0; i < iterations; i++)
        {
            BoxBlurPass(pixels, tempPixels, w, h, radius, true);
            BoxBlurPass(tempPixels, pixels, w, h, radius, false);
        }
    }

    /// <summary>
    /// �{�b�N�X�u���[��1�p�X�i�����܂��͐����j�����s���܂��B
    /// </summary>
    private static void BoxBlurPass(Color32[] source, Color32[] dest, int w, int h, int r, bool isHorizontal)
    {
        float kernelSize = r * 2 + 1;
        if (isHorizontal)
        {
            for (int y = 0; y < h; y++)
            {
                float sumR = 0, sumG = 0, sumB = 0, sumA = 0;
                int rowOffset = y * w;
                for (int i = -r; i <= r; i++) { int idx = Mathf.Clamp(i, 0, w - 1); var c = source[rowOffset + idx]; sumR += c.r; sumG += c.g; sumB += c.b; sumA += c.a; }
                for (int x = 0; x < w; x++)
                {
                    dest[rowOffset + x] = new Color32((byte)(sumR / kernelSize), (byte)(sumG / kernelSize), (byte)(sumB / kernelSize), (byte)(sumA / kernelSize));
                    int oldIdx = Mathf.Clamp(x - r, 0, w - 1); int newIdx = Mathf.Clamp(x + r + 1, 0, w - 1);
                    var oldC = source[rowOffset + oldIdx]; var newC = source[rowOffset + newIdx];
                    sumR += newC.r - oldC.r; sumG += newC.g - oldC.g; sumB += newC.b - oldC.b; sumA += newC.a - oldC.a;
                }
            }
        }
        else
        {
            for (int x = 0; x < w; x++)
            {
                float sumR = 0, sumG = 0, sumB = 0, sumA = 0;
                for (int i = -r; i <= r; i++) { int idx = Mathf.Clamp(i, 0, h - 1); var c = source[idx * w + x]; sumR += c.r; sumG += c.g; sumB += c.b; sumA += c.a; }
                for (int y = 0; y < h; y++)
                {
                    dest[y * w + x] = new Color32((byte)(sumR / kernelSize), (byte)(sumG / kernelSize), (byte)(sumB / kernelSize), (byte)(sumA / kernelSize));
                    int oldIdx = Mathf.Clamp(y - r, 0, h - 1); int newIdx = Mathf.Clamp(y + r + 1, 0, h - 1);
                    var oldC = source[oldIdx * w + x]; var newC = source[newIdx * w + x];
                    sumR += newC.r - oldC.r; sumG += newC.g - oldC.g; sumB += newC.b - oldC.b; sumA += newC.a - oldC.a;
                }
            }
        }
    }

    /// <summary>
    /// �w�肳�ꂽUV���W�̎O�p�`���e�N�X�`���ɓh��Ԃ��܂��B
    /// </summary>
    public static void FillTriangle(Texture2D tex, Vector2 p0, Vector2 p1, Vector2 p2, Color32 color)
    {
        int w = tex.width; int h = tex.height;
        int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x))), 0, w - 1);
        int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x))), 0, w - 1);
        int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y))), 0, h - 1);
        int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y))), 0, h - 1);

        float Area(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

        if (Mathf.Approximately(Area(p0, p1, p2), 0f)) return;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float w0 = Area(p1, p2, p), w1 = Area(p2, p0, p), w2 = Area(p0, p1, p);
                bool hasNeg = (w0 < 0) || (w1 < 0) || (w2 < 0);
                bool hasPos = (w0 > 0) || (w1 > 0) || (w2 > 0);
                if (!(hasNeg && hasPos))
                {
                    tex.SetPixel(x, y, color);
                }
            }
        }
    }

    /// <summary>
    /// UV���W����Ή�����|���S���������܂��i�O���b�h�g�p�Łj�B
    /// </summary>
    /// <param name="mesh">�����Ώۂ̃��b�V���B</param>
    /// <param name="uv">UV���W (0-1)�B</param>
    /// <param name="grid">���O�Ɍv�Z���ꂽUV�O���b�h�B</param>
    /// <param name="gridSize">�O���b�h�̃T�C�Y�B</param>
    /// <returns>���������|���S���̃C���f�b�N�X�B������Ȃ��ꍇ��-1�B</returns>
    public static int FindTriangleFromUV(Mesh mesh, Vector2 uv, Dictionary<Vector2Int, List<int>> grid, int gridSize)
    {
        if (mesh == null || mesh.uv.Length == 0 || grid == null) return -1;

        var cell = new Vector2Int(Mathf.FloorToInt(uv.x * gridSize), Mathf.FloorToInt(uv.y * gridSize));
        if (grid.TryGetValue(cell, out var candidateTriangles))
        {
            var tris = mesh.triangles;
            var uvs = mesh.uv;

            foreach (var triIdx in candidateTriangles)
            {
                Vector2 uv0 = uvs[tris[triIdx * 3 + 0]];
                Vector2 uv1 = uvs[tris[triIdx * 3 + 1]];
                Vector2 uv2 = uvs[tris[triIdx * 3 + 2]];

                if (IsPointInTriangle(uv, uv0, uv1, uv2))
                {
                    return triIdx;
                }
            }
        }
        return -1;
    }

    /// <summary>
    /// UV�|���S���������������邽�߂̃O���b�h���\�z���܂��B
    /// </summary>
    /// <param name="mesh">��͑Ώۂ̃��b�V���B</param>
    /// <param name="gridSize">�O���b�h�̕������B</param>
    /// <returns>UV���W���L�[�Ƃ���|���S���C���f�b�N�X�̃��X�g���������B</returns>
    public static Dictionary<Vector2Int, List<int>> BuildUVTriangleGrid(Mesh mesh, int gridSize)
    {
        var grid = new Dictionary<Vector2Int, List<int>>();
        if (mesh == null || mesh.uv.Length == 0) return grid;

        var tris = mesh.triangles;
        var uvs = mesh.uv;
        int triCount = tris.Length / 3;

        for (int i = 0; i < triCount; i++)
        {
            Vector2 uv0 = uvs[tris[i * 3 + 0]];
            Vector2 uv1 = uvs[tris[i * 3 + 1]];
            Vector2 uv2 = uvs[tris[i * 3 + 2]];

            float minX = Mathf.Min(uv0.x, uv1.x, uv2.x);
            float maxX = Mathf.Max(uv0.x, uv1.x, uv2.x);
            float minY = Mathf.Min(uv0.y, uv1.y, uv2.y);
            float maxY = Mathf.Max(uv0.y, uv1.y, uv2.y);

            int startX = Mathf.FloorToInt(minX * gridSize);
            int endX = Mathf.FloorToInt(maxX * gridSize);
            int startY = Mathf.FloorToInt(minY * gridSize);
            int endY = Mathf.FloorToInt(maxY * gridSize);

            for (int y = startY; y <= endY; y++)
            {
                for (int x = startX; x <= endX; x++)
                {
                    var cell = new Vector2Int(x, y);
                    if (!grid.ContainsKey(cell))
                    {
                        grid[cell] = new List<int>();
                    }
                    grid[cell].Add(i);
                }
            }
        }
        return grid;
    }

    /// <summary>
    /// �w�肵���_���O�p�`�̓����ɂ��邩�ǂ����𔻒肵�܂��i�d�S���W�n�j�B
    /// </summary>
    private static bool IsPointInTriangle(Vector2 p, Vector2 p0, Vector2 p1, Vector2 p2)
    {
        var s = p0.y * p2.x - p0.x * p2.y + (p2.y - p0.y) * p.x + (p0.x - p2.x) * p.y;
        var t = p0.x * p1.y - p0.y * p1.x + (p0.y - p1.y) * p.x + (p1.x - p0.x) * p.y;

        if ((s < 0) != (t < 0) && s != 0 && t != 0)
        {
            return false;
        }

        var A = -p1.y * p2.x + p0.y * (p2.x - p1.x) + p0.x * (p1.y - p2.y) + p1.x * p2.y;
        if (A < 0)
        {
            s = -s;
            t = -t;
            A = -A;
        }
        return s > 0 && t > 0 && (s + t) <= A;
    }

    /// <summary>
    /// 2�̒��_�C���f�b�N�X����A�����Ɉˑ����Ȃ���ӂ�long�^�̃L�[�𐶐����܂��B
    /// </summary>
    private static long GetEdgeKey(int vA, int vB) => vA < vB ? ((long)vA << 32) | (uint)vB : ((long)vB << 32) | (uint)vA;

    /// <summary>
    /// UV���W�̃y�A��Dictionary�̃L�[�Ƃ��Ďg�p���邽�߂̃w���p�[�\���́B
    /// </summary>
    private readonly struct UvEdge : IEquatable<UvEdge>
    {
        private readonly Vector2 u, v;
        public UvEdge(Vector2 u, Vector2 v)
        {
            if (u.x > v.x || (u.x == v.x && u.y > v.y)) { this.u = v; this.v = u; }
            else { this.u = u; this.v = v; }
        }
        public bool Equals(UvEdge other) => u.Equals(other.u) && v.Equals(other.v);
        public override bool Equals(object obj) => obj is UvEdge other && Equals(other);
        public override int GetHashCode() => (u, v).GetHashCode();
    }
}
