// MeshMaskPainterWindow.cs
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class MeshMaskPainterWindow : EditorWindow
{
    [SerializeField]
    private MeshMaskPainterCore m_Core = new MeshMaskPainterCore();

    private Texture2D m_SharpPreviewTex;
    private Texture2D m_BlurredPreviewTex;
    private bool m_IsPreviewBlurred = false;

    private int m_HoverTriangle = -1;
    private int m_LastProcessedTriangle = -1;

    private bool m_TargetFoldout = true;
    private bool m_PaintDisplayFoldout = true;
    private bool m_SelectionToolsFoldout = true;
    private bool m_ExportFoldout = true;

    /// <summary>
    /// メニューからウィンドウを開きます。
    /// </summary>
    [MenuItem("Tools/MMP_Var1.0")]
    public static void ShowWindow()
    {
        var w = GetWindow<MeshMaskPainterWindow>("MMP");
        w.minSize = new Vector2(800, 900);
    }

    /// <summary>
    /// ウィンドウが有効になった際に呼び出されます。
    /// </summary>
    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        Undo.undoRedoPerformed += OnUndoRedo;
        m_Core.UndoRedoPerformed += OnUndoRedo;
    }

    /// <summary>
    /// ウィンドウが無効になった際に呼び出されます。
    /// </summary>
    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        Undo.undoRedoPerformed -= OnUndoRedo;
        m_Core.UndoRedoPerformed -= OnUndoRedo;
        m_Core.OnDisable();
        if (m_SharpPreviewTex != null) DestroyImmediate(m_SharpPreviewTex);
        if (m_BlurredPreviewTex != null) DestroyImmediate(m_BlurredPreviewTex);
    }

    /// <summary>
    /// Undo/Redo操作が実行された際にUIを更新します。
    /// </summary>
    private void OnUndoRedo()
    {
        m_Core.UpdatePreviewTexture(m_SharpPreviewTex);
        Repaint();
    }

    /// <summary>
    /// エディタウィンドウのUIを描画します。
    /// </summary>
    private void OnGUI()
    {
        EditorGUILayout.BeginHorizontal();

        DrawPreviewPanel();
        DrawControlPanel();

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.HelpBox("Sceneビューでメッシュを選択。ペイント=選択追加, 消しゴム=選択解除。\nヒント: Shiftキーを押しながらクリックすると、繋がっているUVアイランド全体を選択できます。", MessageType.Info);
    }

    /// <summary>
    /// シーンビューでの描画とインタラクションを処理します。
    /// </summary>
    /// <param name="view">対象のシーンビュー。</param>
    private void OnSceneGUI(SceneView view)
    {
        if (m_Core == null || !m_Core.IsReady()) return;

        Event e = Event.current;
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        UpdateHoverTriangle(e);
        HandlePaintInput(e);

        if (m_Core.ShowWireframe)
        {
            DrawSelectionOverlays();
        }
        view.Repaint();
    }

    /// <summary>
    /// 左側のプレビューパネルを描画します。
    /// </summary>
    private void DrawPreviewPanel()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
        {
            EditorGUILayout.LabelField("プレビュー (UVマスク)", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope("box", GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                int newPreviewSize = EditorGUILayout.IntPopup("プレビュー解像度", m_Core.PreviewSize, new[] { "512", "1024", "2048" }, new[] { 512, 1024, 2048 });
                if (newPreviewSize != m_Core.PreviewSize || m_SharpPreviewTex == null || m_SharpPreviewTex.width != newPreviewSize)
                {
                    m_Core.PreviewSize = newPreviewSize;
                    CreatePreviewTexture();
                    m_Core.UpdatePreviewTexture(m_SharpPreviewTex);
                }

                Rect rect = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                Texture2D currentPreview = m_IsPreviewBlurred ? m_BlurredPreviewTex : m_SharpPreviewTex;
                if (currentPreview != null)
                {
                    Rect aspectRect = MeshMaskPainterCore.GUITool.GetAspectRect(rect, (float)currentPreview.width / currentPreview.height);
                    EditorGUI.DrawPreviewTexture(aspectRect, currentPreview, null, ScaleMode.ScaleToFit);
                }

                EditorGUILayout.Space(5);
                using (new EditorGUI.DisabledScope(!m_Core.EnableBlur || m_SharpPreviewTex == null))
                {
                    if (!m_IsPreviewBlurred) { if (GUILayout.Button("プレビューにぼかしを適用")) { ApplyBlurToPreview(); m_IsPreviewBlurred = true; } }
                    else { if (GUILayout.Button("ぼかし無しプレビューに戻す")) { m_IsPreviewBlurred = false; } }
                }
            }
        }
    }

    /// <summary>
    /// 右側のコントロールパネルを描画します。
    /// </summary>
    private void DrawControlPanel()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(350)))
        {
            EditorGUILayout.Space();

            m_TargetFoldout = EditorGUILayout.Foldout(m_TargetFoldout, "ターゲット設定", true, EditorStyles.foldoutHeader);
            if (m_TargetFoldout) DrawTargetSettings();

            EditorGUILayout.Space();

            m_SelectionToolsFoldout = EditorGUILayout.Foldout(m_SelectionToolsFoldout, "選択ツール", true, EditorStyles.foldoutHeader);
            if (m_SelectionToolsFoldout) DrawSelectionTools();

            EditorGUILayout.Space();

            m_PaintDisplayFoldout = EditorGUILayout.Foldout(m_PaintDisplayFoldout, "ペイント & 表示設定", true, EditorStyles.foldoutHeader);
            if (m_PaintDisplayFoldout) DrawPaintAndDisplaySettings();

            EditorGUILayout.Space();

            m_ExportFoldout = EditorGUILayout.Foldout(m_ExportFoldout, "エクスポート設定", true, EditorStyles.foldoutHeader);
            if (m_ExportFoldout) DrawExportSettings();
        }
    }

    /// <summary>
    /// 「ターゲット設定」のUIを描画します。
    /// </summary>
    private void DrawTargetSettings()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            var newSmr = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("スキンメッシュレンダラー", m_Core.Smr, typeof(SkinnedMeshRenderer), true);
            if (newSmr != m_Core.Smr)
            {
                Undo.RecordObject(this, "ターゲット変更");
                m_Core.SetTarget(newSmr);
                CreatePreviewTexture();
                m_Core.UpdatePreviewTexture(m_SharpPreviewTex);
            }

            using (new EditorGUI.DisabledScope(m_Core.Smr == null))
            {
                if (GUILayout.Button("各種マップを再構築"))
                {
                    Undo.RecordObject(this, "マップを再構築");
                    m_Core.SetupMeshAndMappings();
                    m_Core.UpdatePreviewTexture(m_SharpPreviewTex);
                }
            }

            EditorGUILayout.Space(4);
            using (new EditorGUI.DisabledScope(m_Core.Smr == null || m_Core.Smr.sharedMaterial == null))
            {
                if (m_Core.BaseTexture == null && m_Core.Smr != null && m_Core.Smr.sharedMaterial != null) m_Core.BaseTexture = m_Core.Smr.sharedMaterial.mainTexture as Texture2D;
                m_Core.BaseTexture = (Texture2D)EditorGUILayout.ObjectField("ベーステクスチャ (参照用)", m_Core.BaseTexture, typeof(Texture2D), false);
                if (m_Core.ShowBaseTextureInPreview && m_Core.BaseTexture != null && !m_Core.BaseTexture.isReadable)
                {
                    EditorGUILayout.HelpBox("プレビュー表示のために、ベーステクスチャの Read/Write を有効にしてください。", MessageType.Warning);
                }
            }
        }
    }

    /// <summary>
    /// 「ペイント & 表示設定」のUIを描画します。
    /// </summary>
    private void DrawPaintAndDisplaySettings()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("ペイントモード", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Toggle(m_Core.PaintMode, "ペイント", "Button")) m_Core.PaintMode = true;
            if (GUILayout.Toggle(!m_Core.PaintMode, "消しゴム", "Button")) m_Core.PaintMode = false;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("ペイント範囲", EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                m_Core.OnlyActiveSubmesh = EditorGUILayout.ToggleLeft("アクティブなサブメッシュのみ", m_Core.OnlyActiveSubmesh, GUILayout.Width(180));
                var (labels, values) = m_Core.GetSubmeshOptions();
                int currentIndex = Array.IndexOf(values, m_Core.ActiveSubmesh); if (currentIndex < 0) currentIndex = 0;
                using (new EditorGUI.DisabledScope(!m_Core.OnlyActiveSubmesh))
                {
                    int nextIndex = EditorGUILayout.Popup(currentIndex, labels);
                    int newActive = values.Length > 0 ? values[Mathf.Clamp(nextIndex, 0, values.Length - 1)] : -1;
                    if (newActive != m_Core.ActiveSubmesh) { m_Core.ActiveSubmesh = newActive; m_Core.UpdatePreviewTexture(m_SharpPreviewTex); }
                }
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("表示設定", EditorStyles.miniBoldLabel);
            //m_Core.EdgeHighlightWidth = EditorGUILayout.Slider("エッジの太さ (px)", m_Core.EdgeHighlightWidth, 0.5f, 6f);
            m_Core.EdgeSelectionColor = EditorGUILayout.ColorField("選択済み箇所の色", m_Core.EdgeSelectionColor);
            m_Core.EdgeHighlightColor = EditorGUILayout.ColorField("選択中のハイライト色", m_Core.EdgeHighlightColor);
            m_Core.ShowWireframe = EditorGUILayout.ToggleLeft("選択済み箇所をワイヤーフレームを表示", m_Core.ShowWireframe);
            m_Core.HoverHighlight = EditorGUILayout.ToggleLeft("ホバー時にハイライト", m_Core.HoverHighlight);
            bool newShowBaseTexture = EditorGUILayout.ToggleLeft("プレビューにベーステクスチャを表示", m_Core.ShowBaseTextureInPreview);
            if (newShowBaseTexture != m_Core.ShowBaseTextureInPreview) { m_Core.ShowBaseTextureInPreview = newShowBaseTexture; m_Core.UpdatePreviewTexture(m_SharpPreviewTex); }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("その他", EditorStyles.miniBoldLabel);
            m_Core.AddTemporaryCollider = EditorGUILayout.ToggleLeft("一時的なMeshColliderを自動追加", m_Core.AddTemporaryCollider);
        }
    }

    /// <summary>
    /// 「選択ツール」のUIを描画します。
    /// </summary>
    private void DrawSelectionTools()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("クリア")) { Undo.RecordObject(this, "選択をクリア"); m_Core.ClearSelection(); }
                using (new EditorGUI.DisabledScope(m_Core.SelectedTriangles.Count == 0)) { if (GUILayout.Button("反転")) { Undo.RecordObject(this, "選択を反転"); m_Core.InvertSelection(); } }
            }
            using (new EditorGUI.DisabledScope(m_Core.SelectedTriangles.Count == 0 || !m_Core.IsAdjacencyMapReady()))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("選択を拡張 (Grow)")) { Undo.RecordObject(this, "選択を拡張"); m_Core.GrowSelection(); }
                    if (GUILayout.Button("選択を縮小 (Shrink)")) { Undo.RecordObject(this, "選択を縮小"); m_Core.ShrinkSelection(); }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("選択を保存")) { m_Core.SaveSelection(); }
                if (GUILayout.Button("選択を読み込み")) { Undo.RecordObject(this, "選択を読み込み"); m_Core.LoadSelection(); }
            }
        }
    }

    /// <summary>
    /// 「エクスポート設定」のUIを描画します。
    /// </summary>
    private void DrawExportSettings()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            m_Core.ExportUseBaseTextureSize = EditorGUILayout.ToggleLeft("ベーステクスチャと同解像度で出力", m_Core.ExportUseBaseTextureSize);
            using (new EditorGUI.DisabledScope(m_Core.ExportUseBaseTextureSize))
            {
                m_Core.ExportWidth = EditorGUILayout.IntPopup("出力解像度 (幅)", m_Core.ExportWidth, new[] { "1024", "2048", "4096" }, new[] { 1024, 2048, 4096 });
                m_Core.ExportHeight = EditorGUILayout.IntPopup("出力解像度 (高さ)", m_Core.ExportHeight, new[] { "1024", "2048", "4096" }, new[] { 1024, 2048, 4096 });
            }
            m_Core.ExportAlphaZeroBackground = EditorGUILayout.ToggleLeft("背景を透過 (アルファ=0)", m_Core.ExportAlphaZeroBackground);
            EditorGUILayout.Space(5);
            using (new EditorGUILayout.VerticalScope("box"))
            {
                m_Core.EnableBlur = EditorGUILayout.ToggleLeft("輪郭のぼかしを有効にする", m_Core.EnableBlur);
                using (new EditorGUI.DisabledScope(!m_Core.EnableBlur))
                {
                    int oldRadius = m_Core.BlurRadius; int oldIterations = m_Core.BlurIterations;
                    m_Core.BlurRadius = EditorGUILayout.IntSlider("ぼかし半径 (px)", m_Core.BlurRadius, 1, 50);
                    m_Core.BlurIterations = EditorGUILayout.IntSlider("ぼかし強度 (反復回数)", m_Core.BlurIterations, 1, 5);
                    if ((oldRadius != m_Core.BlurRadius || oldIterations != m_Core.BlurIterations) && m_IsPreviewBlurred) { ApplyBlurToPreview(); }
                }
            }
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            m_Core.ExportFolder = EditorGUILayout.TextField("デフォルトの出力先", m_Core.ExportFolder);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string chosen = EditorUtility.OpenFolderPanel("デフォルトの出力先を選択", Application.dataPath, "");
                if (!string.IsNullOrEmpty(chosen))
                {
                    if (chosen.StartsWith(Application.dataPath)) m_Core.ExportFolder = "Assets" + chosen.Substring(Application.dataPath.Length);
                    else m_Core.ExportFolder = chosen;
                }
            }
            EditorGUILayout.EndHorizontal();
            using (new EditorGUI.DisabledScope(!m_Core.IsReady()))
            {
                if (GUILayout.Button("マスクを出力 (現在の対象)")) m_Core.ExportMaskTextures();
                if (GUILayout.Button("全サブメッシュを個別に出力")) m_Core.ExportMaskTextures(splitPerSubmesh: true);
            }
        }
    }

    /// <summary>
    /// マウスカーソル下のポリゴンを特定します。
    /// </summary>
    /// <param name="e">現在のイベント。</param>
    private void UpdateHoverTriangle(Event e)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity) && hit.collider?.gameObject == m_Core.Smr.gameObject)
        {
            m_HoverTriangle = hit.triangleIndex;
            if (!m_Core.IsTriangleInActiveScope(m_HoverTriangle))
            {
                m_HoverTriangle = -1;
            }
        }
        else
        {
            m_HoverTriangle = -1;
        }
    }

    /// <summary>
    /// シーンビュー上でのクリックやドラッグ入力を処理します。
    /// </summary>
    /// <param name="e">現在のイベント。</param>
    private void HandlePaintInput(Event e)
    {
        if (m_HoverTriangle < 0)
        {
            if (e.type == EventType.MouseUp && e.button == 0) m_LastProcessedTriangle = -1;
            return;
        }

        if (e.type == EventType.MouseDown && e.shift && e.button == 0 && !e.alt)
        {
            Undo.RecordObject(this, "UVアイランドを選択");
            m_Core.SelectUVIsland(m_HoverTriangle);
            e.Use();
        }
        else if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && !e.alt)
        {
            if (m_HoverTriangle != m_LastProcessedTriangle)
            {
                if (m_Core.PaintMode)
                {
                    Undo.RecordObject(this, "選択を追加");
                    m_Core.AddTriangle(m_HoverTriangle);
                }
                else
                {
                    Undo.RecordObject(this, "選択を消去");
                    m_Core.RemoveTriangle(m_HoverTriangle);
                }
                m_LastProcessedTriangle = m_HoverTriangle;
            }
            e.Use();
        }

        if (e.type == EventType.MouseUp && e.button == 0) m_LastProcessedTriangle = -1;
    }

    /// <summary>
    /// プレビュー用のテクスチャを初期化・再生成します。
    /// </summary>
    private void CreatePreviewTexture()
    {
        if (m_Core.PreviewSize <= 0) m_Core.PreviewSize = 1024;

        if (m_SharpPreviewTex != null) DestroyImmediate(m_SharpPreviewTex);
        if (m_BlurredPreviewTex != null) DestroyImmediate(m_BlurredPreviewTex);

        m_SharpPreviewTex = new Texture2D(m_Core.PreviewSize, m_Core.PreviewSize, TextureFormat.RGBA32, false, true);
        m_SharpPreviewTex.wrapMode = TextureWrapMode.Clamp;
        m_SharpPreviewTex.filterMode = FilterMode.Point;

        m_BlurredPreviewTex = new Texture2D(m_Core.PreviewSize, m_Core.PreviewSize, TextureFormat.RGBA32, false, true);
        m_BlurredPreviewTex.wrapMode = TextureWrapMode.Clamp;
        m_BlurredPreviewTex.filterMode = FilterMode.Bilinear;

        m_IsPreviewBlurred = false;
    }

    /// <summary>
    /// プレビューにぼかしを適用します。
    /// </summary>
    private void ApplyBlurToPreview()
    {
        if (m_SharpPreviewTex == null || m_BlurredPreviewTex == null) return;

        var pixels = m_SharpPreviewTex.GetPixels32();
        MeshAnalysis.ApplyFastBlur(pixels, m_SharpPreviewTex.width, m_SharpPreviewTex.height, m_Core.BlurRadius, m_Core.BlurIterations);
        m_BlurredPreviewTex.SetPixels32(pixels);
        m_BlurredPreviewTex.Apply(false);
    }

    /// <summary>
    /// 選択範囲のワイヤーフレームとホバーハイライトをシーンビューに描画します。
    /// </summary>
    private void DrawSelectionOverlays()
    {
        if (!m_Core.IsReady()) return;

        using (new Handles.DrawingScope(m_Core.Smr.transform.localToWorldMatrix))
        {
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            var verts = m_Core.Mesh.vertices;
            var tris = m_Core.Mesh.triangles;

            List<Vector3> linePoints = new List<Vector3>(m_Core.SelectedTriangles.Count * 6);

            foreach (var triIdx in m_Core.SelectedTriangles)
            {
                if (!m_Core.IsTriangleInActiveScope(triIdx)) continue;

                int i0 = tris[triIdx * 3 + 0];
                int i1 = tris[triIdx * 3 + 1];
                int i2 = tris[triIdx * 3 + 2];

                linePoints.Add(verts[i0]); linePoints.Add(verts[i1]);
                linePoints.Add(verts[i1]); linePoints.Add(verts[i2]);
                linePoints.Add(verts[i2]); linePoints.Add(verts[i0]);
            }

            if (linePoints.Count > 0)
            {
                Handles.color = m_Core.EdgeSelectionColor;
                Handles.DrawLines(linePoints.ToArray());
            }

            if (m_Core.HoverHighlight && m_HoverTriangle >= 0)
            {
                Handles.color = m_Core.EdgeHighlightColor;
                int i0 = tris[m_HoverTriangle * 3 + 0];
                int i1 = tris[m_HoverTriangle * 3 + 1];
                int i2 = tris[m_HoverTriangle * 3 + 2];
                float hoverWidth = Mathf.Max(m_Core.EdgeHighlightWidth + 0.5f, 1f);
                DrawLineAA(verts[i0], verts[i1], hoverWidth);
                DrawLineAA(verts[i1], verts[i2], hoverWidth);
                DrawLineAA(verts[i2], verts[i0], hoverWidth);
            }

            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
            Handles.color = Color.white;
        }

    }

    /// <summary>
    /// 太さを指定できるアンチエイリアス付きの線を描画します。
    /// </summary>
    private void DrawLineAA(Vector3 a, Vector3 b, float width)
    {
        width = Mathf.Max(1f, width);
        Handles.DrawAAPolyLine(width, a, b);
    }
}
