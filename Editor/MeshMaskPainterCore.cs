// MeshMaskPainterCore.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[Serializable]
public class MeshMaskPainterCore
{
    public SkinnedMeshRenderer Smr { get; private set; }
    public Texture2D BaseTexture;

    public bool PaintMode = true;
    public bool OnlyActiveSubmesh = false;
    public int ActiveSubmesh = -1;
    public float EdgeHighlightWidth = 1.5f;
    public bool HoverHighlight = true;
    public bool AddTemporaryCollider = true;
    public bool ShowWireframe = true;
    public bool ShowBaseTextureInPreview = false;

    public int PreviewSize = 1024;
    public bool ExportUseBaseTextureSize = true;
    public int ExportWidth = 2048;
    public int ExportHeight = 2048;
    public bool ExportAlphaZeroBackground = false;
    public string ExportFolder = "Assets";

    public bool EnableBlur = false;
    public int BlurRadius = 8;
    public int BlurIterations = 3;

    [SerializeField]
    public List<int> SelectedTriangles = new List<int>();

    public Mesh Mesh { get; private set; }
    private Mesh m_BakedMesh;

    private MeshCollider m_TempCollider;

    private int[] m_TriangleToSubmesh;
    private Dictionary<int, List<int>> m_AdjacencyMap;
    private Dictionary<int, List<int>> m_UvAdjacencyMap;

    public event Action UndoRedoPerformed;

    /// <summary>
    /// �c�[�����g�p�\�ȏ�Ԃ��i�^�[�Q�b�g���ݒ肳��Ă��邩�j�ǂ������擾���܂��B
    /// </summary>
    public bool IsReady() => Smr != null && Mesh != null;

    /// <summary>
    /// �אڃ}�b�v���\�z�ς݂��ǂ������擾���܂��B
    /// </summary>
    public bool IsAdjacencyMapReady() => m_AdjacencyMap != null;

    /// <summary>
    /// �w�肳�ꂽ�|���S�������݃A�N�e�B�u�ȍ�Ɣ͈͓��ɂ��邩�ǂ����𔻒肵�܂��B
    /// </summary>
    /// <param name="triIdx">���肷��|���S���̃C���f�b�N�X�B</param>
    /// <returns>�͈͓��̏ꍇ��true�B</returns>
    public bool IsTriangleInActiveScope(int triIdx)
    {
        if (triIdx < 0) return false;
        if (OnlyActiveSubmesh && ActiveSubmesh >= 0 && m_TriangleToSubmesh != null)
        {
            return triIdx < m_TriangleToSubmesh.Length && m_TriangleToSubmesh[triIdx] == ActiveSubmesh;
        }
        return true;
    }

    /// <summary>
    /// �V�����^�[�Q�b�g��SkinnedMeshRenderer��ݒ肵�܂��B
    /// </summary>
    /// <param name="newSmr">�V�����^�[�Q�b�g�B</param>
    public void SetTarget(SkinnedMeshRenderer newSmr)
    {
        // �ȑO�ɍ�����ꎞ�x�C�N���b�V��������Δj��
        RemoveBakedMeshIfAny();

        Smr = newSmr;
        SetupMeshAndMappings();
    }

    /// <summary>
    /// �^�[�Q�b�g���b�V�����ݒ肳�ꂽ�ۂ̏������������s���܂��B
    /// </summary>
    public void SetupMeshAndMappings()
    {
        // �O��̃x�C�N���b�V�����N���A�i���� SetTarget �ł��ĂԂ���d�ی��j
        RemoveBakedMeshIfAny();

        if (Smr == null)
        {
            Mesh = null;
            SelectedTriangles.Clear();
            m_TriangleToSubmesh = null;
            m_AdjacencyMap = null;
            m_UvAdjacencyMap = null;
            return;
        }

        // sharedMesh �������ꍇ�͏������Ȃ�
        if (Smr.sharedMesh == null)
        {
            Mesh = null;
            SelectedTriangles.Clear();
            m_TriangleToSubmesh = null;
            m_AdjacencyMap = null;
            m_UvAdjacencyMap = null;
            return;
        }

        // SkinnedMesh �́u���݂̃|�[�Y�v�𔽉f���邽�߂� BakeMesh �����݂�
        try
        {
            var tmp = new Mesh();
            Smr.BakeMesh(tmp);

            // Bake���������Ē��_������Ȃ炻����g���B���s�i���_0�j�Ȃ� sharedMesh ���g���t�H�[���o�b�N
            if (tmp != null && tmp.vertexCount > 0)
            {
                m_BakedMesh = tmp;
                Mesh = m_BakedMesh;
            }
            else
            {
                // ������ BakeMesh �ŋ�Ȃ�j������ sharedMesh ���g��
                if (tmp != null) UnityEngine.Object.DestroyImmediate(tmp);
                Mesh = Smr.sharedMesh;
            }
        }
        catch (Exception)
        {
            // ���S��F��O���o���� sharedMesh ���g��
            RemoveBakedMeshIfAny();
            Mesh = Smr.sharedMesh;
        }

        SelectedTriangles.Clear();
        m_TriangleToSubmesh = MeshAnalysis.BuildTriangleToSubmeshMap(Mesh);
        m_AdjacencyMap = MeshAnalysis.BuildAdjacencyMap(Mesh);
        m_UvAdjacencyMap = MeshAnalysis.BuildUVAdjacencyMap(Mesh);
        EnsureTemporaryCollider();

    }

    /// <summary>
    /// �E�B���h�E�������ɂȂ�ۂ̃N���[���A�b�v�������s���܂��B
    /// </summary>
    public void OnDisable()
    {
        RemoveTemporaryColliderIfAny();
        RemoveBakedMeshIfAny();
    }

    /// <summary>
    /// �w�肳�ꂽ�|���S����I��͈͂ɒǉ����܂��B
    /// </summary>
    /// <param name="triIdx">�ǉ�����|���S���̃C���f�b�N�X�B</param>
    public void AddTriangle(int triIdx)
    {
        if (!SelectedTriangles.Contains(triIdx))
        {
            SelectedTriangles.Add(triIdx);
            UndoRedoPerformed?.Invoke();
        }
    }

    /// <summary>
    /// �w�肳�ꂽ�|���S����I��͈͂���폜���܂��B
    /// </summary>
    /// <param name="triIdx">�폜����|���S���̃C���f�b�N�X�B</param>
    public void RemoveTriangle(int triIdx)
    {
        if (SelectedTriangles.Contains(triIdx))
        {
            SelectedTriangles.Remove(triIdx);
            UndoRedoPerformed?.Invoke();
        }
    }

    /// <summary>
    /// ���ׂĂ̑I�����N���A���܂��B
    /// </summary>
    public void ClearSelection()
    {
        SelectedTriangles.Clear();
        UndoRedoPerformed?.Invoke();
    }

    /// <summary>
    /// ���݂̑I��͈͂𔽓]�����܂��B
    /// </summary>
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

    /// <summary>
    /// ���݂̑I��͈͂��A�אڂ���|���S����1�i�K�g�����܂��B
    /// </summary>
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
                    if (!currentSelection.Contains(neighbor)) newAdditions.Add(neighbor);
                }
            }
        }
        SelectedTriangles.AddRange(newAdditions);
        UndoRedoPerformed?.Invoke();
    }

    /// <summary>
    /// ���݂̑I��͈͂̋��E��1�i�K�����ɏk�����܂��B
    /// </summary>
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

    /// <summary>
    /// �w�肳�ꂽ�J�n�|���S������q�����Ă���UV�A�C�����h�S�̂�I�����܂��B
    /// </summary>
    /// <param name="startTriangle">�T�����J�n����|���S���̃C���f�b�N�X�B</param>
    public void SelectUVIsland(int startTriangle)
    {
        if (m_UvAdjacencyMap == null || !m_UvAdjacencyMap.ContainsKey(startTriangle)) return;

        var q = new Queue<int>();
        q.Enqueue(startTriangle);
        var island = new HashSet<int> { startTriangle };

        while (q.Count > 0)
        {
            int current = q.Dequeue();
            if (m_UvAdjacencyMap.TryGetValue(current, out var neighbors))
            {
                foreach (var neighbor in neighbors)
                {
                    if (island.Add(neighbor)) q.Enqueue(neighbor);
                }
            }
        }

        var currentSelection = new HashSet<int>(SelectedTriangles);
        int addedCount = 0;
        foreach (var tri in island)
        {
            if (currentSelection.Add(tri))
            {
                SelectedTriangles.Add(tri);
                addedCount++;
            }
        }
        if (addedCount > 0) UndoRedoPerformed?.Invoke();
    }

    /// <summary>
    /// ���݂̑I��͈͂��t�@�C���ɕۑ����܂��B
    /// </summary>
    public void SaveSelection()
    {
        if (Smr == null) return;
        string path = EditorUtility.SaveFilePanel("�I��͈͂�ۑ�", "Assets", $"{Smr.name}_selection.mmp", "mmp");
        if (string.IsNullOrEmpty(path)) return;
        File.WriteAllText(path, string.Join(",", SelectedTriangles));
        Debug.Log($"[���b�V���}�X�N�y�C���^�[] �I��͈͂�ۑ����܂���: {path}");
    }

    /// <summary>
    /// �t�@�C������I��͈͂�ǂݍ��݂܂��B
    /// </summary>
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

    /// <summary>
    /// ���݂̑I��͈͂Ɋ�Â��ăv���r���[�e�N�X�`�����X�V���܂��B
    /// </summary>
    /// <param name="previewTex">�X�V�Ώۂ̃e�N�X�`���B</param>
    public void UpdatePreviewTexture(Texture2D previewTex)
    {
        if (Mesh == null || previewTex == null) return;

        if (ShowBaseTextureInPreview && BaseTexture != null && BaseTexture.isReadable)
        {
            RenderTexture rt = RenderTexture.GetTemporary(previewTex.width, previewTex.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
            Graphics.Blit(BaseTexture, rt);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            previewTex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            previewTex.Apply(false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
        }
        else
        {
            var pixels = new Color32[previewTex.width * previewTex.height];
            byte bgA = ExportAlphaZeroBackground ? (byte)0 : (byte)255;
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(0, 0, 0, bgA);
            previewTex.SetPixels32(pixels);
        }

        var uvs = Mesh.uv;
        if (uvs == null || uvs.Length == 0) { previewTex.Apply(false); return; }

        var selectionColor = ShowBaseTextureInPreview ? new Color32(255, 255, 255, 180) : new Color32(255, 255, 255, 255);
        int width = previewTex.width, height = previewTex.height;
        var tris = Mesh.triangles;

        foreach (var triIdx in SelectedTriangles)
        {
            if (triIdx < 0 || triIdx * 3 + 2 >= tris.Length) continue;
            if (OnlyActiveSubmesh && ActiveSubmesh >= 0 && m_TriangleToSubmesh != null)
            {
                if (triIdx >= m_TriangleToSubmesh.Length || m_TriangleToSubmesh[triIdx] != ActiveSubmesh) continue;
            }
            int i0 = tris[triIdx * 3 + 0], i1 = tris[triIdx * 3 + 1], i2 = tris[triIdx * 3 + 2];
            Vector2 uv0 = new Vector2(uvs[i0].x * width, uvs[i0].y * height);
            Vector2 uv1 = new Vector2(uvs[i1].x * width, uvs[i1].y * height);
            Vector2 uv2 = new Vector2(uvs[i2].x * width, uvs[i2].y * height);
            MeshAnalysis.FillTriangle(previewTex, uv0, uv1, uv2, selectionColor);
        }
        previewTex.Apply(false);
    }

    /// <summary>
    /// �}�X�N�摜���G�N�X�|�[�g���܂��B
    /// </summary>
    /// <param name="splitPerSubmesh">�T�u���b�V�����ƂɃt�@�C���𕪊����ďo�͂��邩�ǂ����B</param>
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

            string safeName = Smr != null ? Smr.name : "Mesh";
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

    /// <summary>
    /// �T�u���b�V���I��p��UI�I�v�V�����i���x���ƒl�j���擾���܂��B
    /// </summary>
    public (string[], int[]) GetSubmeshOptions()
    {
        int subCount = Mesh != null ? Mesh.subMeshCount : 0;
        var labels = new List<string> { "���ׂ�" };
        for (int i = 0; i < subCount; i++) labels.Add($"�T�u���b�V�� {i}");

        var values = new List<int> { -1 };
        for (int i = 0; i < subCount; i++) values.Add(i);

        return (labels.ToArray(), values.ToArray());
    }

    /// <summary>
    /// ���C�L���X�g�̂��߂Ɉꎞ�I��MeshCollider��t�^���܂��B
    /// </summary>
    private void EnsureTemporaryCollider()
    {
        if (!AddTemporaryCollider || Smr == null) return;
        var col = Smr.GetComponent<Collider>();
        if (col == null)
        {
            m_TempCollider = Smr.gameObject.AddComponent<MeshCollider>();
            m_TempCollider.sharedMesh = Mesh;
            m_TempCollider.convex = false;
        }
    }

    /// <summary>
    /// �t�^�����ꎞ�I��MeshCollider���폜���܂��B
    /// </summary>
    private void RemoveTemporaryColliderIfAny()
    {
        if (m_TempCollider != null)
        {
            UnityEngine.Object.DestroyImmediate(m_TempCollider);
            m_TempCollider = null;
        }
    }

    /// <summary>
    /// �쐬�����x�C�N�p�̈ꎞMesh������Δj������i���������[�N�h�~�j�B
    /// </summary>
    private void RemoveBakedMeshIfAny()
    {
        if (m_BakedMesh != null)
        {
            UnityEngine.Object.DestroyImmediate(m_BakedMesh);
            m_BakedMesh = null;
        }
    }

    /// <summary>
    /// GUI�֘A�̃w���p�[���\�b�h��񋟂��܂��B
    /// </summary>
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
