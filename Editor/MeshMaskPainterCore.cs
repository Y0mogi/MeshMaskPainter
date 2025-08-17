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

[Serializable]
public class MeshMaskPainterCore
{
    public SkinnedMeshRenderer Smr { get; private set; }
    public Texture2D BaseTexture;

    public bool PaintMode = true;
    public bool OnlyActiveSubmesh = false;
    public int ActiveSubmesh = -1;
    public float EdgeHighlightWidth = 1.5f;
    public Color EdgeHighlightColor = Color.red;
    public Color EdgeSelectionColor = Color.blue;
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

    public Texture2D TargetTextureForWrite;
    public TargetChannel TargetTextureChannel = TargetChannel.A;
    public TargetChannel TargetVertexColorChannel = TargetChannel.A;


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
    /// ツールが使用可能な状態か（ターゲットが設定されているか）どうかを取得します。
    /// </summary>
    public bool IsReady() => Smr != null && Mesh != null;

    /// <summary>
    /// 隣接マップが構築済みかどうかを取得します。
    /// </summary>
    public bool IsAdjacencyMapReady() => m_AdjacencyMap != null;

    /// <summary>
    /// 指定されたポリゴンが現在アクティブな作業範囲内にあるかどうかを判定します。
    /// </summary>
    /// <param name="triIdx">判定するポリゴンのインデックス。</param>
    /// <returns>範囲内の場合はtrue。</returns>
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
    /// 新しいターゲットのSkinnedMeshRendererを設定します。
    /// </summary>
    /// <param name="newSmr">新しいターゲット。</param>
    public void SetTarget(SkinnedMeshRenderer newSmr)
    {
        RemoveBakedMeshIfAny();
        Smr = newSmr;
        SetupMeshAndMappings();
    }

    /// <summary>
    /// ターゲットメッシュが設定された際の初期化処理を行います。
    /// </summary>
    public void SetupMeshAndMappings()
    {
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

        if (Smr.sharedMesh == null)
        {
            Mesh = null;
            SelectedTriangles.Clear();
            m_TriangleToSubmesh = null;
            m_AdjacencyMap = null;
            m_UvAdjacencyMap = null;
            return;
        }

        try
        {
            var tmp = new Mesh();
            Smr.BakeMesh(tmp);

            if (tmp != null && tmp.vertexCount > 0)
            {
                m_BakedMesh = tmp;
                Mesh = m_BakedMesh;
            }
            else
            {
                if (tmp != null) UnityEngine.Object.DestroyImmediate(tmp);
                Mesh = Smr.sharedMesh;
            }
        }
        catch (Exception)
        {
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
    /// ウィンドウが無効になる際のクリーンアップ処理を行います。
    /// </summary>
    public void OnDisable()
    {
        RemoveTemporaryColliderIfAny();
        RemoveBakedMeshIfAny();
    }

    /// <summary>
    /// 指定されたポリゴンを選択範囲に追加します。
    /// </summary>
    /// <param name="triIdx">追加するポリゴンのインデックス。</param>
    public void AddTriangle(int triIdx)
    {
        if (!SelectedTriangles.Contains(triIdx))
        {
            SelectedTriangles.Add(triIdx);
            UndoRedoPerformed?.Invoke();
        }
    }

    /// <summary>
    /// 指定されたポリゴンを選択範囲から削除します。
    /// </summary>
    /// <param name="triIdx">削除するポリゴンのインデックス。</param>
    public void RemoveTriangle(int triIdx)
    {
        if (SelectedTriangles.Contains(triIdx))
        {
            SelectedTriangles.Remove(triIdx);
            UndoRedoPerformed?.Invoke();
        }
    }

    /// <summary>
    /// すべての選択をクリアします。
    /// </summary>
    public void ClearSelection()
    {
        SelectedTriangles.Clear();
        UndoRedoPerformed?.Invoke();
    }

    /// <summary>
    /// 現在の選択範囲を反転させます。
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
    /// 現在の選択範囲を、隣接するポリゴンに1段階拡張します。
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
    /// 現在の選択範囲の境界を1段階内側に縮小します。
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
    /// 指定された開始ポリゴンから繋がっているUVアイランド全体を選択します。
    /// </summary>
    /// <param name="startTriangle">探索を開始するポリゴンのインデックス。</param>
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
    /// 現在の選択範囲をファイルに保存します。
    /// </summary>
    public void SaveSelection()
    {
        if (Smr == null) return;
        string path = EditorUtility.SaveFilePanel("選択範囲を保存", "Assets", $"{Smr.name}_selection.mmp", "mmp");
        if (string.IsNullOrEmpty(path)) return;
        File.WriteAllText(path, string.Join(",", SelectedTriangles));
        Debug.Log($"[メッシュマスクペインター] 選択範囲を保存しました: {path}");
    }

    /// <summary>
    /// ファイルから選択範囲を読み込みます。
    /// </summary>
    public void LoadSelection()
    {
        string path = EditorUtility.OpenFilePanel("選択範囲を読み込み", "Assets", "mmp");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try
        {
            SelectedTriangles = File.ReadAllText(path).Split(',').Select(int.Parse).ToList();
            Debug.Log($"[メッシュマスクペインター] 選択範囲を読み込みました: {path}");
            UndoRedoPerformed?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogError($"[メッシュマスクペインター] 選択範囲ファイルの読み込みに失敗しました: {e.Message}");
        }
    }

    /// <summary>
    /// 現在の選択範囲に基づいてプレビューテクスチャを更新します。
    /// </summary>
    /// <param name="previewTex">更新対象のテクスチャ。</param>
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
    /// マスク画像をエクスポートします。
    /// </summary>
    /// <param name="splitPerSubmesh">サブメッシュごとにファイルを分割して出力するかどうか。</param>
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

            string path = EditorUtility.SaveFilePanel("マスク画像を保存", initialDir, defaultFileName, "png");

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
                    Debug.Log($"[メッシュマスクペインター] エクスポート完了: {relativePath}");
                }
                else
                {
                    Debug.Log($"[メッシュマスクペインター] 外部フォルダにエクスポート完了: {path}");
                }
                exportedCount++;
            }

            UnityEngine.Object.DestroyImmediate(tex);
        }

        if (exportedCount > 0)
        {
            EditorUtility.DisplayDialog("メッシュマスクペインター", $"{exportedCount}点のマスク画像をエクスポートしました。", "OK");
        }
    }

    /// <summary>
    /// マスク情報を頂点カラーにベイクします。
    /// </summary>
    public void BakeToVertexColor()
    {
        if (!IsReady()) return;
        if (TargetVertexColorChannel == 0)
        {
            EditorUtility.DisplayDialog("エラー", "書き込み先のチャンネルが選択されていません。", "OK");
            return;
        }
        if (!EditorUtility.DisplayDialog("頂点カラーへのベイク",
            "この操作は新しいメッシュアセットを作成します。\n" +
            "元のメッシュアセットは変更されませんが、シーン内のオブジェクトのメッシュ参照は新しいアセットに置き換えられます。\n" +
            "続行しますか？", "はい", "いいえ"))
        {
            return;
        }

        string originalPath = AssetDatabase.GetAssetPath(Smr.sharedMesh);
        string newPath = EditorUtility.SaveFilePanelInProject(
            "新しいメッシュアセットを保存",
            $"{Path.GetFileNameWithoutExtension(originalPath)}_VColor.asset",
            "asset",
            "新しいメッシュアセットの保存先を選択してください");

        if (string.IsNullOrEmpty(newPath)) return;

        Mesh newMesh = UnityEngine.Object.Instantiate(Mesh);
        newMesh.name = Path.GetFileNameWithoutExtension(newPath);

        Color[] colors = newMesh.vertexCount > 0 && newMesh.colors != null && newMesh.colors.Length == newMesh.vertexCount
            ? newMesh.colors
            : new Color[newMesh.vertexCount];
        if (colors.Length == 0)
        {
            Debug.LogError("[メッシュマスクペインター] メッシュに頂点データが存在しないため、頂点カラーをベイクできません。");
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

        Smr.sharedMesh = newMesh;

        SetupMeshAndMappings();

        EditorUtility.DisplayDialog("成功", $"頂点カラーをベイクし、新しいメッシュアセットを保存しました:\n{newPath}", "OK");
    }

    /// <summary>
    /// マスク情報を既存のテクスチャの特定チャンネルに書き込みます。
    /// </summary>
    public void WriteToTextureChannel()
    {
        if (TargetTextureForWrite == null)
        {
            EditorUtility.DisplayDialog("エラー", "書き込み先のテクスチャが指定されていません。", "OK");
            return;
        }
        if (TargetTextureChannel == 0)
        {
            EditorUtility.DisplayDialog("エラー", "書き込み先のチャンネルが選択されていません。", "OK");
            return;
        }

        string path = AssetDatabase.GetAssetPath(TargetTextureForWrite);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null || !importer.isReadable)
        {
            EditorUtility.DisplayDialog("エラー", "書き込み先のテクスチャの Read/Write を有効にしてください。", "OK");
            return;
        }

        RenderTexture tmpRT = RenderTexture.GetTemporary(
            TargetTextureForWrite.width,
            TargetTextureForWrite.height,
            0,
            RenderTextureFormat.Default,
            RenderTextureReadWrite.Linear);

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

        EditorUtility.DisplayDialog("成功", $"指定チャンネルへの書き込みが完了しました:\n{path}", "OK");
    }

    /// <summary>
    /// サブメッシュ選択用のUIオプション（ラベルと値）を取得します。
    /// </summary>
    public (string[], int[]) GetSubmeshOptions()
    {
        int subCount = Mesh != null ? Mesh.subMeshCount : 0;
        var labels = new List<string> { "すべて" };
        for (int i = 0; i < subCount; i++) labels.Add($"サブメッシュ {i}");

        var values = new List<int> { -1 };
        for (int i = 0; i < subCount; i++) values.Add(i);

        return (labels.ToArray(), values.ToArray());
    }

    /// <summary>
    /// レイキャストのために一時的なMeshColliderを付与します。
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
        else if (col == m_TempCollider)
        {
            m_TempCollider.sharedMesh = Mesh;
        }
    }

    /// <summary>
    /// 付与した一時的なMeshColliderを削除します。
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
    /// 作成したベイク用の一時Meshがあれば破棄する（メモリリーク防止）。
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
    /// GUI関連のヘルパーメソッドを提供します。
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