// MeshMaskPainterCore.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[Flags]
public enum TargetChannel
{
    R = 1 << 0, // 1
    G = 1 << 1, // 2
    B = 1 << 2, // 4
    A = 1 << 3  // 8
}

public enum ToolMode
{
    Select,
    Paint
}

[Serializable]
public class MeshMaskPainterCore
{
    public Renderer TargetRenderer { get; private set; }
    public Texture2D BaseTexture;

    public bool PaintMode = true; // ���̃t�B�[���h��UI����폜����܂������A���ʌ݊����̂��߂Ɏc�����Ƃ��\�ł�
    private Color32[] m_OriginalPixels; // ���̃e�N�X�`���̃s�N�Z���f�[�^��ێ�

    public bool OnlyActiveSubmesh = false;
    public int ActiveSubmesh = -1;
    public Color EdgeHighlightColor = Color.red;
    public Color EdgeSelectionColor = Color.blue;
    public bool HoverHighlight = true;
    public bool AddTemporaryCollider = true;
    public bool ShowWireframe = true;
    public bool CullBackfaceWireframe = true;
    public bool ShowBaseTextureInPreview = false;

    public int PreviewSize = 1024;
    public Color PreviewSelectColor = Color.red;
    public bool ExportUseBaseTextureSize = true;
    public int ExportWidth = 2048;
    public int ExportHeight = 2048;
    public bool ExportAlphaZeroBackground = false;
    public string ExportFolder = "Assets";

    public bool EnableBlur = false;
    public int BlurRadius = 8;
    public int BlurIterations = 3;

    public Texture2D TargetTextureForWrite;
    public TargetChannel TargetTextureChannel = TargetChannel.A;
    public TargetChannel TargetVertexColorChannel = TargetChannel.A;

    public bool IsolateEnabled = false;

    [SerializeField]
    public List<int> SelectedTriangles = new List<int>();

    public ToolMode CurrentTool = ToolMode.Select;
    public Color PaintColor = Color.white;
    public Texture2D m_PaintableTexture { get; private set; }

    public Mesh Mesh { get; private set; }
    private Mesh m_BakedMesh;
    private MeshCollider m_TempCollider;

    private int[] m_TriangleToSubmesh;
    private Dictionary<int, List<int>> m_AdjacencyMap;
    private Dictionary<int, List<int>> m_UvAdjacencyMap;
    private Dictionary<Vector2Int, List<int>> m_UvTriangleGrid;
    private const int UV_GRID_SIZE = 32;

    public event Action UndoRedoPerformed;

    public bool IsReady() => TargetRenderer != null && Mesh != null;
    public bool IsAdjacencyMapReady() => m_AdjacencyMap != null;

    public bool IsTriangleInActiveScope(int triIdx)
    {
        if (triIdx < 0) return false;
        if (OnlyActiveSubmesh && ActiveSubmesh >= 0 && m_TriangleToSubmesh != null)
        {
            return triIdx < m_TriangleToSubmesh.Length && m_TriangleToSubmesh[triIdx] == ActiveSubmesh;
        }
        return true;
    }

    public void SetTarget(Renderer newRenderer)
    {
        RemoveBakedMeshIfAny();
        if (m_PaintableTexture != null)
        {
            UnityEngine.Object.DestroyImmediate(m_PaintableTexture);
            m_PaintableTexture = null;
        }
        TargetRenderer = newRenderer;
        SetupMeshAndMappings();
    }

    public void SetupMeshAndMappings()
    {
        RemoveBakedMeshIfAny();
        Mesh = null;
        m_UvTriangleGrid = null;

        if (TargetRenderer == null)
        {
            SelectedTriangles.Clear();
            m_TriangleToSubmesh = null;
            m_AdjacencyMap = null;
            m_UvAdjacencyMap = null;
            return;
        }

        Mesh sourceMesh = null;
        if (TargetRenderer is SkinnedMeshRenderer smr)
        {
            sourceMesh = smr.sharedMesh;
            if (sourceMesh != null)
            {
                try
                {
                    var tmp = new Mesh();
                    smr.BakeMesh(tmp);
                    if (tmp != null && tmp.vertexCount > 0)
                    {
                        m_BakedMesh = tmp;
                        Mesh = m_BakedMesh;
                    }
                    else
                    {
                        if (tmp != null) UnityEngine.Object.DestroyImmediate(tmp);
                        Mesh = sourceMesh;
                    }
                }
                catch (Exception)
                {
                    RemoveBakedMeshIfAny();
                    Mesh = sourceMesh;
                }
            }
        }
        else if (TargetRenderer is MeshRenderer mr)
        {
            var mf = mr.GetComponent<MeshFilter>();
            if (mf != null)
            {
                sourceMesh = mf.sharedMesh;
                Mesh = sourceMesh;
            }
        }

        if (Mesh == null)
        {
            SelectedTriangles.Clear();
            m_TriangleToSubmesh = null;
            m_AdjacencyMap = null;
            m_UvAdjacencyMap = null;
            return;
        }

        SelectedTriangles.Clear();
        m_TriangleToSubmesh = MeshAnalysis.BuildTriangleToSubmeshMap(Mesh);
        m_AdjacencyMap = MeshAnalysis.BuildAdjacencyMap(Mesh);
        m_UvAdjacencyMap = MeshAnalysis.BuildUVAdjacencyMap(Mesh);
        m_UvTriangleGrid = MeshAnalysis.BuildUVTriangleGrid(Mesh, UV_GRID_SIZE);
        EnsureTemporaryCollider();
    }

    public void OnDisable()
    {
        RemoveTemporaryColliderIfAny();
        RemoveBakedMeshIfAny();
        if (m_PaintableTexture != null) UnityEngine.Object.DestroyImmediate(m_PaintableTexture);
    }

    public void AddTriangle(int triIdx)
    {
        ModifySelection(() =>
        {
            if (IsTriangleInActiveScope(triIdx) && !SelectedTriangles.Contains(triIdx))
            {
                SelectedTriangles.Add(triIdx);
            }
        });
    }

    public void RemoveTriangle(int triIdx)
    {
        ModifySelection(() =>
        {
            if (SelectedTriangles.Contains(triIdx))
            {
                SelectedTriangles.Remove(triIdx);
            }
        });

    }

    public void ClearSelection()
    {
        ModifySelection(() => SelectedTriangles.Clear());
    }

    public void UpdateTextureFromSelection()
    {
        if (m_PaintableTexture == null || m_OriginalPixels == null) return;

        // �e�N�X�`������U�A�y�C���g�J�n���̏�ԂɊ��S�ɖ߂�
        m_PaintableTexture.SetPixels32(m_OriginalPixels);

        var uvs = Mesh.uv;
        var tris = Mesh.triangles;
        int width = m_PaintableTexture.width;
        int height = m_PaintableTexture.height;

        // ���ݑI������Ă���|���S���������w��F�ŏ�`������
        foreach (var triIdx in SelectedTriangles)
        {
            if (triIdx < 0 || triIdx * 3 + 2 >= tris.Length) continue;
            int i0 = tris[triIdx * 3 + 0], i1 = tris[triIdx * 3 + 1], i2 = tris[triIdx * 3 + 2];
            Vector2 uv0 = new Vector2(uvs[i0].x * width, uvs[i0].y * height);
            Vector2 uv1 = new Vector2(uvs[i1].x * width, uvs[i1].y * height);
            Vector2 uv2 = new Vector2(uvs[i2].x * width, uvs[i2].y * height);
            MeshAnalysis.FillTriangle(m_PaintableTexture, uv0, uv1, uv2, PaintColor);
        }

        m_PaintableTexture.Apply(false);
    }


    private void ModifySelection(Action modification)
    {
        modification.Invoke();
        if (CurrentTool == ToolMode.Paint && m_PaintableTexture != null)
        {
            UpdateTextureFromSelection();
        }
        UndoRedoPerformed?.Invoke();
    }


    public void InvertSelection()
    {
        if (Mesh == null) return;
        int triCount = Mesh.triangles.Length / 3;
        var newList = new List<int>();
        for (int t = 0; t < triCount; t++)
        {
            if (OnlyActiveSubmesh && ActiveSubmesh >= 0 && m_TriangleToSubmesh != null)
            {
                if (t >= m_TriangleToSubmesh.Length || m_TriangleToSubmesh[t] != ActiveSubmesh) continue;
            }
            if (!SelectedTriangles.Contains(t))
            {
                newList.Add(t);
            }
        }
        if (OnlyActiveSubmesh && ActiveSubmesh >= 0 && m_TriangleToSubmesh != null)
        {
            foreach (var t in SelectedTriangles)
            {
                if (t < m_TriangleToSubmesh.Length && m_TriangleToSubmesh[t] != ActiveSubmesh)
                {
                    newList.Add(t);
                }
            }
        }
        SelectedTriangles = newList;
        UndoRedoPerformed?.Invoke();
    }

    public void GrowSelection()
    {
        if (m_AdjacencyMap == null || SelectedTriangles.Count == 0) return;
        var newAdditions = new HashSet<int>();
        var currentSelection = new HashSet<int>(SelectedTriangles);
        foreach (var triIdx in SelectedTriangles)
        {
            if (m_AdjacencyMap.TryGetValue(triIdx, out var neighbors))
            {
                foreach (var neighbor in neighbors)
                {
                    if (!currentSelection.Contains(neighbor) && IsTriangleInActiveScope(neighbor)) newAdditions.Add(neighbor);
                }
            }
        }
        SelectedTriangles.AddRange(newAdditions);
        UndoRedoPerformed?.Invoke();
    }

    public void ShrinkSelection()
    {
        if (m_AdjacencyMap == null || SelectedTriangles.Count == 0) return;
        var toRemove = new List<int>();
        var currentSelection = new HashSet<int>(SelectedTriangles);
        foreach (var triIdx in SelectedTriangles)
        {
            if (m_AdjacencyMap.TryGetValue(triIdx, out var neighbors))
            {
                foreach (var neighbor in neighbors)
                {
                    if (!currentSelection.Contains(neighbor))
                    {
                        toRemove.Add(triIdx);
                        break;
                    }
                }
            }
        }
        SelectedTriangles = SelectedTriangles.Except(toRemove).ToList();
        UndoRedoPerformed?.Invoke();
    }

    public HashSet<int> GetUVIslandTriangles(int startTriangle)
    {
        var islandTriangles = new HashSet<int>();
        if (m_UvAdjacencyMap == null || !m_UvAdjacencyMap.ContainsKey(startTriangle))
        {
            return islandTriangles;
        }
        var q = new Queue<int>();
        q.Enqueue(startTriangle);
        islandTriangles.Add(startTriangle);
        while (q.Count > 0)
        {
            int current = q.Dequeue();
            if (m_UvAdjacencyMap.TryGetValue(current, out var neighbors))
            {
                foreach (var neighbor in neighbors)
                {
                    if (islandTriangles.Add(neighbor))
                    {
                        q.Enqueue(neighbor);
                    }
                }
            }
        }
        return islandTriangles;
    }

    public void ToggleUVIslandSelection(int startTriangle)
    {
        ModifySelection(() =>
        {
            var islandTriangles = GetUVIslandTriangles(startTriangle);
            if (islandTriangles.Count == 0) return;
            var currentSelection = new HashSet<int>(SelectedTriangles);
            bool isAnyPartOfIslandSelected = islandTriangles.Overlaps(currentSelection);

            if (isAnyPartOfIslandSelected)
            {
                SelectedTriangles.RemoveAll(islandTriangles.Contains);
            }
            else
            {
                foreach (var triIdx in islandTriangles)
                {
                    if (IsTriangleInActiveScope(triIdx) && !currentSelection.Contains(triIdx))
                    {
                        SelectedTriangles.Add(triIdx);
                    }
                }
            }
        });
    }

    public void SaveSelection()
    {
        if (TargetRenderer == null) return;
        string path = EditorUtility.SaveFilePanel("�I��͈͂�ۑ�", "Assets", $"{TargetRenderer.name}_selection.mmp", "mmp");
        if (string.IsNullOrEmpty(path)) return;
        File.WriteAllText(path, string.Join(",", SelectedTriangles));
        Debug.Log($"[���b�V���}�X�N�y�C���^�[] �I��͈͂�ۑ����܂���: {path}");
    }

    public void LoadSelection()
    {
        string path = EditorUtility.OpenFilePanel("�I��͈͂�ǂݍ���", "Assets", "mmp");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try
        {
            SelectedTriangles = File.ReadAllText(path).Split(',').Select(int.Parse).ToList();
            Debug.Log($"[���b�V���}�X�N�y�C���^�[] �I��͈͂�ǂݍ��݂܂���: {path}");
            UndoRedoPerformed?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogError($"[���b�V���}�X�N�y�C���^�[] �I��͈̓t�@�C���̓ǂݍ��݂Ɏ��s���܂���: {e.Message}");
        }
    }

    public void UpdatePreviewTexture(Texture2D previewTex)
    {
        if (Mesh == null || previewTex == null) return;

        RenderTexture previous = RenderTexture.active;
        RenderTexture rt = RenderTexture.GetTemporary(previewTex.width, previewTex.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
        RenderTexture.active = rt;

        if (m_PaintableTexture != null)
        {
            Graphics.Blit(m_PaintableTexture, rt);
        }
        else if (ShowBaseTextureInPreview && BaseTexture != null)
        {
            Graphics.Blit(BaseTexture, rt);
        }
        else
        {
            var pixels = new Color32[previewTex.width * previewTex.height];
            byte bgA = ExportAlphaZeroBackground ? (byte)0 : (byte)255;
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(0, 0, 0, bgA);
            previewTex.SetPixels32(pixels);
            previewTex.Apply(false);
            Graphics.Blit(previewTex, rt);
        }

        previewTex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);

        var uvs = Mesh.uv;
        if (uvs != null && uvs.Length > 0 && SelectedTriangles.Count > 0)
        {
            Color selectionColor = (CurrentTool == ToolMode.Select)
                ? Color.white
                : PaintColor;

            int width = previewTex.width, height = previewTex.height;
            var tris = Mesh.triangles;

            foreach (var triIdx in SelectedTriangles)
            {
                if (triIdx < 0 || triIdx * 3 + 2 >= tris.Length) continue;
                if (!IsTriangleInActiveScope(triIdx)) continue;

                int i0 = tris[triIdx * 3 + 0], i1 = tris[triIdx * 3 + 1], i2 = tris[triIdx * 3 + 2];
                Vector2 uv0 = new Vector2(uvs[i0].x * width, uvs[i0].y * height);
                Vector2 uv1 = new Vector2(uvs[i1].x * width, uvs[i1].y * height);
                Vector2 uv2 = new Vector2(uvs[i2].x * width, uvs[i2].y * height);
                MeshAnalysis.FillTriangle(previewTex, uv0, uv1, uv2, selectionColor);
            }
        }
        previewTex.Apply(false);

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
    }

    public void ExportMaskTextures(bool splitPerSubmesh = false)
    {
        if (Mesh == null) return;
        int w, h;
        if (ExportUseBaseTextureSize && BaseTexture != null) { w = BaseTexture.width; h = BaseTexture.height; }
        else { w = Mathf.Max(4, ExportWidth); h = Mathf.Max(4, ExportHeight); }
        var targets = new List<int>();
        if (splitPerSubmesh) { for (int s = 0; s < Mesh.subMeshCount; s++) targets.Add(s); }
        else { targets.Add(ActiveSubmesh); }
        int exportedCount = 0;
        foreach (var target in targets)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[w * h];
            byte bgA = ExportAlphaZeroBackground ? (byte)0 : (byte)255;
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(0, 0, 0, bgA);
            tex.SetPixels32(pixels);
            tex.Apply(false);
            var uvs = Mesh.uv;
            var tris = Mesh.triangles;
            foreach (var triIdx in SelectedTriangles)
            {
                if (triIdx < 0 || triIdx * 3 + 2 >= tris.Length) continue;
                if (target >= 0 && m_TriangleToSubmesh != null)
                {
                    if (triIdx >= m_TriangleToSubmesh.Length || m_TriangleToSubmesh[triIdx] != target) continue;
                }
                int i0 = tris[triIdx * 3 + 0], i1 = tris[triIdx * 3 + 1], i2 = tris[triIdx * 3 + 2];
                Vector2 uv0 = new Vector2(uvs[i0].x * w, uvs[i0].y * h), uv1 = new Vector2(uvs[i1].x * w, uvs[i1].y * h), uv2 = new Vector2(uvs[i2].x * w, uvs[i2].y * h);
                MeshAnalysis.FillTriangle(tex, uv0, uv1, uv2, new Color32(255, 255, 255, 255));
            }
            tex.Apply(false);
            if (EnableBlur && BlurRadius > 0 && BlurIterations > 0)
            {
                var sharpPixels = tex.GetPixels32();
                MeshAnalysis.ApplyFastBlur(sharpPixels, w, h, BlurRadius, BlurIterations);
                tex.SetPixels32(sharpPixels);
            }
            tex.Apply(false);
            string safeName = TargetRenderer != null ? TargetRenderer.name : "Mesh";
            string subLabel = target < 0 ? "All" : $"S{target}";
            string defaultFileName = $"{safeName}_Mask_{subLabel}{(EnableBlur ? "_Blurred" : "")}.png";
            string initialDir = Path.GetFullPath(ExportFolder);
            string path = EditorUtility.SaveFilePanel("�}�X�N�摜��ۑ�", initialDir, defaultFileName, "png");
            if (!string.IsNullOrEmpty(path))
            {
                File.WriteAllBytes(path, tex.EncodeToPNG());
                if (path.StartsWith(Application.dataPath))
                {
                    string relativePath = "Assets" + path.Substring(Application.dataPath.Length);
                    AssetDatabase.Refresh();
                    var imp = AssetImporter.GetAtPath(relativePath) as TextureImporter;
                    if (imp != null)
                    {
                        imp.textureType = TextureImporterType.Default;
                        imp.sRGBTexture = false;
                        imp.alphaIsTransparency = ExportAlphaZeroBackground;
                        imp.mipmapEnabled = true;
                        imp.streamingMipmaps = true;
                        imp.wrapMode = TextureWrapMode.Clamp;
                        imp.SaveAndReimport();
                    }
                    Debug.Log($"[���b�V���}�X�N�y�C���^�[] �G�N�X�|�[�g����: {relativePath}");
                }
                else
                {
                    Debug.Log($"[���b�V���}�X�N�y�C���^�[] �O���t�H���_�ɃG�N�X�|�[�g����: {path}");
                }
                exportedCount++;
            }
            UnityEngine.Object.DestroyImmediate(tex);
        }
        if (exportedCount > 0)
        {
            EditorUtility.DisplayDialog("���b�V���}�X�N�y�C���^�[", $"{exportedCount}�_�̃}�X�N�摜���G�N�X�|�[�g���܂����B", "OK");
        }
    }

    public void BakeToVertexColor()
    {
        if (!IsReady()) return;
        if (TargetVertexColorChannel == 0)
        {
            EditorUtility.DisplayDialog("�G���[", "�������ݐ�̃`�����l�����I������Ă��܂���B", "OK");
            return;
        }
        if (!EditorUtility.DisplayDialog("���_�J���[�ւ̃x�C�N", "���̑���͐V�������b�V���A�Z�b�g���쐬���܂�...", "�͂�", "������"))
        {
            return;
        }
        Mesh sourceMesh = null;
        if (TargetRenderer is SkinnedMeshRenderer smr) sourceMesh = smr.sharedMesh;
        else if (TargetRenderer is MeshRenderer mr) sourceMesh = mr.GetComponent<MeshFilter>()?.sharedMesh;
        if (sourceMesh == null)
        {
            EditorUtility.DisplayDialog("�G���[", "�x�C�N���̃��b�V����������܂���B", "OK");
            return;
        }
        string originalPath = AssetDatabase.GetAssetPath(sourceMesh);
        string newPath = EditorUtility.SaveFilePanelInProject("�V�������b�V���A�Z�b�g��ۑ�", $"{Path.GetFileNameWithoutExtension(originalPath)}_VColor.asset", "asset", "�V�������b�V���A�Z�b�g�̕ۑ����I�����Ă�������");
        if (string.IsNullOrEmpty(newPath)) return;
        Mesh newMesh = UnityEngine.Object.Instantiate(Mesh);
        newMesh.name = Path.GetFileNameWithoutExtension(newPath);
        Color[] colors = newMesh.vertexCount > 0 && newMesh.colors != null && newMesh.colors.Length == newMesh.vertexCount
            ? newMesh.colors
            : new Color[newMesh.vertexCount];
        if (colors.Length == 0)
        {
            Debug.LogError("[���b�V���}�X�N�y�C���^�[] ���b�V���ɒ��_�f�[�^�����݂��Ȃ����߁A���_�J���[���x�C�N�ł��܂���B");
            UnityEngine.Object.DestroyImmediate(newMesh);
            return;
        }
        var tris = newMesh.triangles;
        var selectedVerts = new HashSet<int>();
        foreach (var triIdx in SelectedTriangles)
        {
            if (IsTriangleInActiveScope(triIdx))
            {
                selectedVerts.Add(tris[triIdx * 3 + 0]);
                selectedVerts.Add(tris[triIdx * 3 + 1]);
                selectedVerts.Add(tris[triIdx * 3 + 2]);
            }
        }
        foreach (int vertIdx in selectedVerts)
        {
            if (TargetVertexColorChannel.HasFlag(TargetChannel.R)) colors[vertIdx].r = 1f;
            if (TargetVertexColorChannel.HasFlag(TargetChannel.G)) colors[vertIdx].g = 1f;
            if (TargetVertexColorChannel.HasFlag(TargetChannel.B)) colors[vertIdx].b = 1f;
            if (TargetVertexColorChannel.HasFlag(TargetChannel.A)) colors[vertIdx].a = 1f;
        }
        newMesh.colors = colors;
        AssetDatabase.CreateAsset(newMesh, newPath);
        AssetDatabase.SaveAssets();
        if (TargetRenderer is SkinnedMeshRenderer smr2) smr2.sharedMesh = newMesh;
        else if (TargetRenderer is MeshRenderer mr2) mr2.GetComponent<MeshFilter>().sharedMesh = newMesh;
        SetupMeshAndMappings();
        EditorUtility.DisplayDialog("����", $"���_�J���[���x�C�N���A�V�������b�V���A�Z�b�g��ۑ����܂���:\n{newPath}", "OK");
    }

    public void WriteToTextureChannel()
    {
        if (TargetTextureForWrite == null)
        {
            EditorUtility.DisplayDialog("�G���[", "�������ݐ�̃e�N�X�`�����w�肳��Ă��܂���B", "OK");
            return;
        }
        if (TargetTextureChannel == 0)
        {
            EditorUtility.DisplayDialog("�G���[", "�������ݐ�̃`�����l�����I������Ă��܂���B", "OK");
            return;
        }
        string path = AssetDatabase.GetAssetPath(TargetTextureForWrite);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null || !importer.isReadable)
        {
            EditorUtility.DisplayDialog("�G���[", "�������ݐ�̃e�N�X�`���� Read/Write ��L���ɂ��Ă��������B", "OK");
            return;
        }
        RenderTexture tmpRT = RenderTexture.GetTemporary(TargetTextureForWrite.width, TargetTextureForWrite.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
        Graphics.Blit(TargetTextureForWrite, tmpRT);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = tmpRT;
        Texture2D writableTex = new Texture2D(TargetTextureForWrite.width, TargetTextureForWrite.height, TextureFormat.RGBA32, false);
        writableTex.ReadPixels(new Rect(0, 0, tmpRT.width, tmpRT.height), 0, 0);
        writableTex.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(tmpRT);
        var maskTex = new Texture2D(TargetTextureForWrite.width, TargetTextureForWrite.height, TextureFormat.RGBA32, false, true);
        var pixels = new Color32[maskTex.width * maskTex.height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(0, 0, 0, 0);
        maskTex.SetPixels32(pixels);
        var uvs = Mesh.uv;
        var tris = Mesh.triangles;
        foreach (var triIdx in SelectedTriangles)
        {
            if (!IsTriangleInActiveScope(triIdx)) continue;
            int i0 = tris[triIdx * 3 + 0], i1 = tris[triIdx * 3 + 1], i2 = tris[triIdx * 3 + 2];
            Vector2 uv0 = new Vector2(uvs[i0].x * maskTex.width, uvs[i0].y * maskTex.height);
            Vector2 uv1 = new Vector2(uvs[i1].x * maskTex.width, uvs[i1].y * maskTex.height);
            Vector2 uv2 = new Vector2(uvs[i2].x * maskTex.width, uvs[i2].y * maskTex.height);
            MeshAnalysis.FillTriangle(maskTex, uv0, uv1, uv2, Color.white);
        }
        maskTex.Apply(false);
        if (EnableBlur && BlurRadius > 0 && BlurIterations > 0)
        {
            var sharpPixels = maskTex.GetPixels32();
            MeshAnalysis.ApplyFastBlur(sharpPixels, maskTex.width, maskTex.height, BlurRadius, BlurIterations);
            maskTex.SetPixels32(sharpPixels);
            maskTex.Apply(false);
        }
        var targetPixels = writableTex.GetPixels();
        var maskPixels = maskTex.GetPixels();
        for (int i = 0; i < targetPixels.Length; i++)
        {
            if (TargetTextureChannel.HasFlag(TargetChannel.R)) targetPixels[i].r = maskPixels[i].r;
            if (TargetTextureChannel.HasFlag(TargetChannel.G)) targetPixels[i].g = maskPixels[i].g;
            if (TargetTextureChannel.HasFlag(TargetChannel.B)) targetPixels[i].b = maskPixels[i].b;
            if (TargetTextureChannel.HasFlag(TargetChannel.A)) targetPixels[i].a = maskPixels[i].r;
        }
        writableTex.SetPixels(targetPixels);
        writableTex.Apply(false);
        File.WriteAllBytes(path, writableTex.EncodeToPNG());
        AssetDatabase.Refresh();
        UnityEngine.Object.DestroyImmediate(maskTex);
        UnityEngine.Object.DestroyImmediate(writableTex);
        EditorUtility.DisplayDialog("����", $"�w��`�����l���ւ̏������݂��������܂���:\n{path}", "OK");
    }

    public (string[], int[]) GetSubmeshOptions()
    {
        int subCount = Mesh != null ? Mesh.subMeshCount : 0;
        var labels = new List<string> { "���ׂ�" };
        for (int i = 0; i < subCount; i++) labels.Add($"�T�u���b�V�� {i}");
        var values = new List<int> { -1 };
        for (int i = 0; i < subCount; i++) values.Add(i);
        return (labels.ToArray(), values.ToArray());
    }

    public int FindTriangleFromUV(Vector2 uv)
    {
        if (Mesh == null) return -1;
        return MeshAnalysis.FindTriangleFromUV(Mesh, uv, m_UvTriangleGrid, UV_GRID_SIZE);
    }

    public Mesh CreateIsolatedMesh()
    {
        if (Mesh == null || SelectedTriangles.Count == 0) return null;
        var newMesh = new Mesh();
        var currentVerts = Mesh.vertices;
        var currentNormals = Mesh.normals;
        var currentTangents = Mesh.tangents;
        var currentUVs = Mesh.uv;
        var currentColors = Mesh.colors;
        var originalTris = Mesh.triangles;
        var newVerts = new List<Vector3>();
        var newNormals = new List<Vector3>();
        var newTangents = new List<Vector4>();
        var newUVs = new List<Vector2>();
        var newColors = new List<Color>();
        var newTris = new List<int>();
        var vertMap = new Dictionary<int, int>();
        int newVertIdx = 0;
        foreach (var triIdx in SelectedTriangles)
        {
            if (!IsTriangleInActiveScope(triIdx)) continue;
            for (int i = 0; i < 3; i++)
            {
                int originalVertIdx = originalTris[triIdx * 3 + i];
                if (vertMap.TryGetValue(originalVertIdx, out int mappedIdx))
                {
                    newTris.Add(mappedIdx);
                }
                else
                {
                    newVerts.Add(currentVerts[originalVertIdx]);
                    if (currentNormals.Length > originalVertIdx) newNormals.Add(currentNormals[originalVertIdx]);
                    if (currentTangents.Length > originalVertIdx) newTangents.Add(currentTangents[originalVertIdx]);
                    if (currentUVs.Length > originalVertIdx) newUVs.Add(currentUVs[originalVertIdx]);
                    if (currentColors.Length > originalVertIdx) newColors.Add(currentColors[originalVertIdx]);
                    vertMap[originalVertIdx] = newVertIdx;
                    newTris.Add(newVertIdx);
                    newVertIdx++;
                }
            }
        }
        newMesh.vertices = newVerts.ToArray();
        newMesh.triangles = newTris.ToArray();
        if (newNormals.Count == newVerts.Count) newMesh.normals = newNormals.ToArray(); else newMesh.RecalculateNormals();
        if (newTangents.Count == newVerts.Count) newMesh.tangents = newTangents.ToArray(); else newMesh.RecalculateTangents();
        if (newUVs.Count == newVerts.Count) newMesh.uv = newUVs.ToArray();
        if (newColors.Count == newVerts.Count) newMesh.colors = newColors.ToArray();
        return newMesh;
    }

    private void EnsureTemporaryCollider()
    {
        if (!AddTemporaryCollider || TargetRenderer == null) return;
        var col = TargetRenderer.GetComponent<Collider>();
        if (col == null)
        {
            m_TempCollider = TargetRenderer.gameObject.AddComponent<MeshCollider>();
            m_TempCollider.sharedMesh = Mesh;
            m_TempCollider.convex = false;
        }
        else if (col == m_TempCollider)
        {
            m_TempCollider.sharedMesh = Mesh;
        }
    }

    private void RemoveTemporaryColliderIfAny()
    {
        if (m_TempCollider != null)
        {
            UnityEngine.Object.DestroyImmediate(m_TempCollider);
            m_TempCollider = null;
        }
    }

    private void RemoveBakedMeshIfAny()
    {
        if (m_BakedMesh != null)
        {
            UnityEngine.Object.DestroyImmediate(m_BakedMesh);
            m_BakedMesh = null;
        }
    }

    public bool CreatePaintableTexture()
    {
        if (BaseTexture == null)
        {
            Debug.LogError("[MMP] �y�C���g�̌��ƂȂ�x�[�X�e�N�X�`�����ݒ肳��Ă��܂���B");
            return false;
        }
        if (m_PaintableTexture != null) UnityEngine.Object.DestroyImmediate(m_PaintableTexture);

        // ���e�N�X�`���̃C���|�[�^�[�ݒ肩��A�F��ԁisRGB�����j�A���j���擾����
        string path = AssetDatabase.GetAssetPath(BaseTexture);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

        // �C���|�[�^�[���擾�ł��Ȃ��ꍇ�i��F�����^�C�������e�N�X�`���Ȃǁj���l��
        bool isSrgb = true;
        if (importer != null)
        {
            isSrgb = importer.sRGBTexture;
        }
        else
        {
            // �C���|�[�^�[���Ȃ��ꍇ��Unity�̕W���I�ȋ����ɍ��킹��
            // Texture.format���琄������͕̂��G�Ȃ��߁A�����̏ꍇ�ɓ��Ă͂܂�sRGB���f�t�H���g�Ƃ���
            Debug.LogWarning("[MMP] ���e�N�X�`���̃C���|�[�^�[���擾�ł��܂���ł����BsRGB�Ƃ��Ĉ����܂��B");
        }

        // �擾�����F��Ԃɍ��킹��RenderTexture���쐬
        RenderTextureReadWrite readWriteMode = isSrgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear;
        RenderTexture rt = RenderTexture.GetTemporary(BaseTexture.width, BaseTexture.height, 0, RenderTextureFormat.Default, readWriteMode);

        // ���e�N�X�`����RenderTexture�ɃR�s�[
        Graphics.Blit(BaseTexture, rt);

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;

        // �y�C���g�p�e�N�X�`�����A���̐F��Ԑݒ�������p���ŁA����RGBA32�t�H�[�}�b�g�ō쐬
        // new Texture2D�̍Ō�̈����́u���j�A�J���[�X�y�[�X���H�v�Ȃ̂ŁAisSrgb�̋t��n��
        m_PaintableTexture = new Texture2D(BaseTexture.width, BaseTexture.height, TextureFormat.RGBA32, false, !isSrgb);

        // RenderTexture����s�N�Z����ǂݍ���
        m_PaintableTexture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        m_PaintableTexture.Apply();

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        // RGBA32�t�H�[�}�b�g�ɂȂ����e�N�X�`������A���̃s�N�Z������ۑ�����
        m_OriginalPixels = m_PaintableTexture.GetPixels32();

        Debug.Log("[MMP] �y�C���g�p�̃e�N�X�`�����������܂����B");
        UndoRedoPerformed?.Invoke();
        return true;

    }

    public void SavePaintableTexture()
    {
        if (m_PaintableTexture == null)
        {
            EditorUtility.DisplayDialog("�G���[", "�ۑ�����y�C���g�ς݃e�N�X�`��������܂���B", "OK");
            return;
        }

        // �ۑ��̒��O�ɁA���݂̑I��͈͂��e�N�X�`���ɓK�p����
        UpdateTextureFromSelection();

        string safeName = TargetRenderer != null ? TargetRenderer.name : "Mesh";
        string path = EditorUtility.SaveFilePanel("�y�C���g�����e�N�X�`����ۑ�", ExportFolder, $"{safeName}_Painted.png", "png");
        if (!string.IsNullOrEmpty(path))
        {
            File.WriteAllBytes(path, m_PaintableTexture.EncodeToPNG());
            Debug.Log($"[MMP] �e�N�X�`����ۑ����܂���: {path}");
            if (path.StartsWith(Application.dataPath))
            {
                AssetDatabase.Refresh();
                string relativePath = "Assets" + path.Substring(Application.dataPath.Length);
                var imp = AssetImporter.GetAtPath(relativePath) as TextureImporter;
                if (imp != null)
                {
                    imp.sRGBTexture = true;
                    imp.alphaIsTransparency = true;
                    imp.SaveAndReimport();
                }
            }
        }

    }

    public void PaintTriangles(IEnumerable<int> triangleIndices)
    {
        if (m_PaintableTexture == null)
        {
            Debug.LogWarning("�y�C���g�Ώۂ̃e�N�X�`��������܂���B�y�C���g���[�h��UI����u�y�C���g�p�e�N�X�`���������v���Ă��������B");
            return;
        }
        var uvs = Mesh.uv;
        var tris = Mesh.triangles;
        int width = m_PaintableTexture.width;
        int height = m_PaintableTexture.height;
        foreach (var triIdx in triangleIndices)
        {
            if (triIdx < 0 || triIdx * 3 + 2 >= tris.Length) continue;
            int i0 = tris[triIdx * 3 + 0], i1 = tris[triIdx * 3 + 1], i2 = tris[triIdx * 3 + 2];
            Vector2 uv0 = new Vector2(uvs[i0].x * width, uvs[i0].y * height);
            Vector2 uv1 = new Vector2(uvs[i1].x * width, uvs[i1].y * height);
            Vector2 uv2 = new Vector2(uvs[i2].x * width, uvs[i2].y * height);
            MeshAnalysis.FillTriangle(m_PaintableTexture, uv0, uv1, uv2, PaintColor);
        }
        m_PaintableTexture.Apply(false);
        UndoRedoPerformed?.Invoke();
    }

    internal static class GUITool
    {
        public static Rect GetAspectRect(Rect rect, float aspect)
        {
            float previewWidth = rect.width;
            float previewHeight = rect.width / aspect;
            if (previewHeight > rect.height)
            {
                previewHeight = rect.height;
                previewWidth = rect.height * aspect;
            }
            return new Rect(
                rect.x + (rect.width - previewWidth) / 2,
                rect.y + (rect.height - previewHeight) / 2,
                previewWidth,
                previewHeight);
        }
    }
}