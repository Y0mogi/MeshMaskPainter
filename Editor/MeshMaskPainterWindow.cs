// MeshMaskPainterWindow.cs
using System;
using System.Collections.Generic;
using System.Linq;
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
    private int m_LastProcessedTriangle2D = -1;

    private int m_ExportTab = 0;

    private Mesh m_IsolatedMesh;
    private bool m_IsolateMeshDirty = true;
    private bool m_Is2DPaintEnabled = false;

    private Vector2 m_ScrollPos;
    private Vector2 m_RendererListScrollPos;

    private GameObject m_TargetGameObject;
    private List<Renderer> m_FoundRenderers = new List<Renderer>();
    private readonly Dictionary<Renderer, Texture2D> m_PreviewTextures = new Dictionary<Renderer, Texture2D>();
    private bool m_ShowRendererSelection = false;
    private const int RENDERER_PREVIEW_SIZE = 64;

    /// <summary>
    /// メニューからウィンドウを開きます。
    /// </summary>
    [MenuItem("Tools/Mesh Mask Painter")]
    public static void ShowWindow()
    {
        var w = GetWindow<MeshMaskPainterWindow>("MMP");
        w.minSize = new Vector2(800, 600);
    }

    /// <summary>
    /// ウィンドウが有効になった際に呼び出されます。
    /// </summary>
    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        Undo.undoRedoPerformed += OnUndoRedo;
        m_Core.UndoRedoPerformed += OnUndoRedo;
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
    }

    /// <summary>
    /// ウィンドウが無効になった際に呼び出されます。
    /// </summary>
    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        Undo.undoRedoPerformed -= OnUndoRedo;
        m_Core.UndoRedoPerformed -= OnUndoRedo;
        EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        m_Core.OnDisable();
        if (m_SharpPreviewTex != null) DestroyImmediate(m_SharpPreviewTex);
        if (m_BlurredPreviewTex != null) DestroyImmediate(m_BlurredPreviewTex);
        if (m_IsolatedMesh != null) DestroyImmediate(m_IsolatedMesh);

        ClearPreviewTextures();
        SetIsolationMode(false);
    }

    /// <summary>
    /// Undo/Redo操作が実行された際にUIを更新します。
    /// </summary>
    private void OnUndoRedo()
    {
        m_Core.UpdatePreviewTexture(m_SharpPreviewTex);
        m_IsolateMeshDirty = true;
        Repaint();
    }

    /// <summary>
    /// Hierarchyの変更を検知してターゲットの有無をチェックします。
    /// </summary>
    private void OnHierarchyChanged()
    {
        if (m_Core.TargetRenderer == null)
        {
            m_Core.SetTarget(null);
            Repaint();
        }
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

        if (m_Core.IsolateEnabled)
        {
            if (m_IsolateMeshDirty)
            {
                if (m_IsolatedMesh != null) DestroyImmediate(m_IsolatedMesh);
                m_IsolatedMesh = m_Core.CreateIsolatedMesh();
                m_IsolateMeshDirty = false;
            }

            if (m_IsolatedMesh != null)
            {
                GL.PushMatrix();
                GL.MultMatrix(m_Core.TargetRenderer.transform.localToWorldMatrix);
                m_Core.TargetRenderer.sharedMaterial.SetPass(0);
                Graphics.DrawMeshNow(m_IsolatedMesh, Vector3.zero, Quaternion.identity);
                GL.PopMatrix();
            }
        }

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
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUILayout.LabelField("UVプレビュー", GUILayout.Width(100));
                GUILayout.FlexibleSpace();
                m_Is2DPaintEnabled = GUILayout.Toggle(m_Is2DPaintEnabled, new GUIContent("2Dペイント", "UVプレビュー上でペイントを有効にします"), EditorStyles.toolbarButton, GUILayout.Width(80));
            }

            using (new EditorGUILayout.VerticalScope("box", GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                Rect rect = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                Texture2D currentPreview = m_IsPreviewBlurred ? m_BlurredPreviewTex : m_SharpPreviewTex;
                if (currentPreview != null)
                {
                    Rect aspectRect = MeshMaskPainterCore.GUITool.GetAspectRect(rect, (float)currentPreview.width / currentPreview.height);
                    EditorGUI.DrawPreviewTexture(aspectRect, currentPreview, null, ScaleMode.ScaleToFit);

                    if (m_Is2DPaintEnabled)
                    {
                        Handle2DPaintInput(aspectRect);
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                int newPreviewSize = EditorGUILayout.IntPopup(m_Core.PreviewSize, new[] { "512", "1024", "2048" }, new[] { 512, 1024, 2048 }, GUILayout.Width(60));
                if (newPreviewSize != m_Core.PreviewSize || m_SharpPreviewTex == null || m_SharpPreviewTex.width != newPreviewSize)
                {
                    m_Core.PreviewSize = newPreviewSize;
                    CreatePreviewTexture();
                    m_Core.UpdatePreviewTexture(m_SharpPreviewTex);
                }

                using (new EditorGUI.DisabledScope(!m_Core.EnableBlur || m_SharpPreviewTex == null))
                {
                    if (!m_IsPreviewBlurred)
                    {
                        if (GUILayout.Button("ぼかしプレビュー", EditorStyles.toolbarButton)) { ApplyBlurToPreview(); m_IsPreviewBlurred = true; }
                    }
                    else
                    {
                        if (GUILayout.Button("通常プレビュー", EditorStyles.toolbarButton)) { m_IsPreviewBlurred = false; }
                    }
                }
            }
        }
    }

    /// <summary>
    /// 右側のコントロールパネルを描画します。
    /// </summary>
    private void DrawControlPanel()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(380)))
        {
            m_ScrollPos = EditorGUILayout.BeginScrollView(m_ScrollPos);

            EditorGUILayout.LabelField("ターゲット設定", EditorStyles.boldLabel);
            DrawTargetSettings();

            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("選択ツール", EditorStyles.boldLabel);
            DrawSelectionTools();

            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("ペイント & 表示設定", EditorStyles.boldLabel);
            DrawPaintAndDisplaySettings();

            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("エクスポート", EditorStyles.boldLabel);
            DrawExportSettings();

            EditorGUILayout.EndScrollView();
        }
    }

    /// <summary>
    /// 「ターゲット設定」のUIを描画します。
    /// </summary>
    private void DrawTargetSettings()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUI.BeginChangeCheck();
            m_TargetGameObject = (GameObject)EditorGUILayout.ObjectField("ターゲットオブジェクト", m_TargetGameObject, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
            {
                m_Core.SetTarget(null);
                ClearPreviewTextures();
                m_FoundRenderers.Clear();

                if (m_TargetGameObject != null)
                {
                    var renderers = m_TargetGameObject.GetComponentsInChildren<Renderer>(true);
                    foreach (var r in renderers)
                    {
                        if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                        {
                            m_FoundRenderers.Add(r);
                        }
                        else if (r is MeshRenderer mr && mr.GetComponent<MeshFilter>()?.sharedMesh != null)
                        {
                            m_FoundRenderers.Add(r);
                        }
                    }
                    m_ShowRendererSelection = m_FoundRenderers.Count > 0;
                }
                else
                {
                    m_ShowRendererSelection = false;
                }
            }

            if (m_Core.TargetRenderer != null)
            {
                EditorGUILayout.LabelField("現在のターゲット:", m_Core.TargetRenderer.gameObject.name);
            }
            else if (m_TargetGameObject != null)
            {
                EditorGUILayout.HelpBox("オブジェクトから使用するレンダラーを選択してください。", MessageType.Info);
            }

            if (m_TargetGameObject != null)
            {
                m_ShowRendererSelection = EditorGUILayout.Foldout(m_ShowRendererSelection, $"見つかったレンダラー ({m_FoundRenderers.Count})", true);
                if (m_ShowRendererSelection)
                {
                    using (var scrollView = new EditorGUILayout.ScrollViewScope(m_RendererListScrollPos, GUILayout.Height(Mathf.Min(m_FoundRenderers.Count * 72, 220))))
                    {
                        m_RendererListScrollPos = scrollView.scrollPosition;
                        foreach (var renderer in m_FoundRenderers)
                        {
                            if (renderer == null) continue;
                            using (new EditorGUILayout.HorizontalScope("box"))
                            {
                                if (!m_PreviewTextures.ContainsKey(renderer) || m_PreviewTextures[renderer] == null)
                                {
                                    m_PreviewTextures[renderer] = GenerateRendererPreview(renderer);
                                }
                                var previewTex = m_PreviewTextures[renderer];

                                Rect previewRect = GUILayoutUtility.GetRect(64, 64);
                                if (previewTex != null)
                                {
                                    GUI.DrawTexture(previewRect, previewTex, ScaleMode.ScaleToFit);
                                }
                                else
                                {
                                    GUI.Label(previewRect, "No Preview");
                                }

                                using (new EditorGUILayout.VerticalScope())
                                {
                                    EditorGUILayout.LabelField(renderer.gameObject.name, EditorStyles.boldLabel);
                                    EditorGUILayout.LabelField(renderer.GetType().Name);
                                    if (GUILayout.Button("このレンダラーを選択"))
                                    {
                                        OnRendererSelected(renderer);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            using (new EditorGUI.DisabledScope(m_Core.TargetRenderer == null))
            {
                if (GUILayout.Button("メッシュ情報を再構築"))
                {
                    Undo.RecordObject(this, "マップを再構築");
                    m_Core.SetupMeshAndMappings();
                    m_Core.UpdatePreviewTexture(m_SharpPreviewTex);
                    m_IsolateMeshDirty = true;
                }
            }
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
    /// 「ペイント & 表示設定」のUIを描画します。
    /// </summary>
    private void DrawPaintAndDisplaySettings()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            m_Core.PaintMode = GUILayout.Toolbar(m_Core.PaintMode ? 0 : 1, new[] { "ペイント", "消しゴム" }) == 0;
            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                m_Core.OnlyActiveSubmesh = EditorGUILayout.ToggleLeft("アクティブなサブメッシュのみ", m_Core.OnlyActiveSubmesh, GUILayout.Width(180));
                var (labels, values) = m_Core.GetSubmeshOptions();
                int currentIndex = Array.IndexOf(values, m_Core.ActiveSubmesh); if (currentIndex < 0) currentIndex = 0;
                using (new EditorGUI.DisabledScope(!m_Core.OnlyActiveSubmesh || !m_Core.IsReady()))
                {
                    int nextIndex = EditorGUILayout.Popup(currentIndex, labels);
                    int newActive = values.Length > 0 ? values[Mathf.Clamp(nextIndex, 0, values.Length - 1)] : -1;
                    if (newActive != m_Core.ActiveSubmesh) { m_Core.ActiveSubmesh = newActive; m_Core.UpdatePreviewTexture(m_SharpPreviewTex); }
                }
            }
            EditorGUILayout.Space(8);

            m_Core.ShowWireframe = EditorGUILayout.ToggleLeft("選択箇所のワイヤーフレーム表示", m_Core.ShowWireframe);
            m_Core.HoverHighlight = EditorGUILayout.ToggleLeft("ホバー箇所をハイライト", m_Core.HoverHighlight);

            bool newIsolate = EditorGUILayout.ToggleLeft("選択箇所のみ分離表示 (Isolate)", m_Core.IsolateEnabled);
            if (newIsolate != m_Core.IsolateEnabled)
            {
                SetIsolationMode(newIsolate);
            }

            EditorGUILayout.Space(5);
            m_Core.EdgeSelectionColor = EditorGUILayout.ColorField("選択ワイヤーフレームの色", m_Core.EdgeSelectionColor);
            m_Core.EdgeHighlightColor = EditorGUILayout.ColorField("ハイライトの色", m_Core.EdgeHighlightColor);
            EditorGUILayout.Space(5);

            using (new EditorGUI.DisabledScope(m_Core.TargetRenderer == null || m_Core.TargetRenderer.sharedMaterial == null))
            {
                if (m_Core.BaseTexture == null && m_Core.TargetRenderer != null && m_Core.TargetRenderer.sharedMaterial != null) m_Core.BaseTexture = m_Core.TargetRenderer.sharedMaterial.mainTexture as Texture2D;
                m_Core.BaseTexture = (Texture2D)EditorGUILayout.ObjectField("ベーステクスチャ (参照用)", m_Core.BaseTexture, typeof(Texture2D), false);

                bool newShowBaseTexture = EditorGUILayout.ToggleLeft("プレビューにベーステクスチャを表示", m_Core.ShowBaseTextureInPreview);
                if (newShowBaseTexture != m_Core.ShowBaseTextureInPreview) { m_Core.ShowBaseTextureInPreview = newShowBaseTexture; m_Core.UpdatePreviewTexture(m_SharpPreviewTex); }

                if (m_Core.ShowBaseTextureInPreview && m_Core.BaseTexture != null && !m_Core.BaseTexture.isReadable)
                {
                    EditorGUILayout.HelpBox("プレビュー表示のために、ベーステクスチャの Read/Write を有効にしてください。", MessageType.Warning);
                }
            }
            EditorGUILayout.Space(5);

            m_Core.AddTemporaryCollider = EditorGUILayout.ToggleLeft("一時的なMeshColliderを自動追加", m_Core.AddTemporaryCollider);
        }
    }

    /// <summary>
    /// 「エクスポート」セクションのUIを描画します。
    /// </summary>
    private void DrawExportSettings()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            m_ExportTab = GUILayout.Toolbar(m_ExportTab, new[] { "マスク出力", "頂点カラー", "テクスチャ書込" });
            EditorGUILayout.Space(5);

            switch (m_ExportTab)
            {
                case 0: DrawExportMaskTextureUI(); break;
                case 1: DrawBakeVertexColorUI(); break;
                case 2: DrawWriteToTextureUI(); break;
            }
        }
    }

    /// <summary>
    /// 「マスク出力」タブのUIを描画します。
    /// </summary>
    private void DrawExportMaskTextureUI()
    {
        EditorGUILayout.LabelField("新規マスクテクスチャとして出力します。", EditorStyles.wordWrappedLabel);
        EditorGUILayout.Space(5);
        m_Core.ExportUseBaseTextureSize = EditorGUILayout.ToggleLeft("ベーステクスチャと同解像度で出力", m_Core.ExportUseBaseTextureSize);
        using (new EditorGUI.DisabledScope(m_Core.ExportUseBaseTextureSize))
        {
            m_Core.ExportWidth = EditorGUILayout.IntPopup("出力解像度 (幅)", m_Core.ExportWidth, new[] { "1024", "2048", "4096" }, new[] { 1024, 2048, 4096 });
            m_Core.ExportHeight = EditorGUILayout.IntPopup("出力解像度 (高さ)", m_Core.ExportHeight, new[] { "1024", "2048", "4096" }, new[] { 1024, 2048, 4096 });
        }
        m_Core.ExportAlphaZeroBackground = EditorGUILayout.ToggleLeft("背景を透過 (アルファ=0)", m_Core.ExportAlphaZeroBackground);

        DrawBlurSettings();

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

    /// <summary>
    /// 「頂点カラー」タブのUIを描画します。
    /// </summary>
    private void DrawBakeVertexColorUI()
    {
        EditorGUILayout.HelpBox("選択範囲を頂点カラーに書き込み、新しいメッシュアセットを作成します。", MessageType.Info);
        m_Core.TargetVertexColorChannel = (TargetChannel)EditorGUILayout.EnumFlagsField("書き込み先チャンネル", m_Core.TargetVertexColorChannel);
        EditorGUILayout.Space(5);
        using (new EditorGUI.DisabledScope(!m_Core.IsReady() || m_Core.TargetRenderer.GetComponent<MeshFilter>() == null && m_Core.TargetRenderer.GetComponent<SkinnedMeshRenderer>() == null))
        {
            if (GUILayout.Button("頂点カラーへベイク"))
            {
                m_Core.BakeToVertexColor();
            }
        }
    }

    /// <summary>
    /// 「テクスチャ書込」タブのUIを描画します。
    /// </summary>
    private void DrawWriteToTextureUI()
    {
        EditorGUILayout.HelpBox("既存のテクスチャの特定チャンネルにマスク情報を上書きします。", MessageType.Info);
        m_Core.TargetTextureForWrite = (Texture2D)EditorGUILayout.ObjectField("書き込み先テクスチャ", m_Core.TargetTextureForWrite, typeof(Texture2D), false);
        if (m_Core.TargetTextureForWrite != null && !m_Core.TargetTextureForWrite.isReadable)
        {
            EditorGUILayout.HelpBox("テクスチャのインポート設定で Read/Write を有効にしてください。", MessageType.Warning);
        }
        m_Core.TargetTextureChannel = (TargetChannel)EditorGUILayout.EnumFlagsField("書き込み先チャンネル", m_Core.TargetTextureChannel);
        DrawBlurSettings();
        using (new EditorGUI.DisabledScope(!m_Core.IsReady() || m_Core.TargetTextureForWrite == null || !m_Core.TargetTextureForWrite.isReadable))
        {
            if (GUILayout.Button("指定チャンネルへ書き込み"))
            {
                m_Core.WriteToTextureChannel();
            }
        }
    }

    /// <summary>
    /// ぼかし設定のUIを描画します。
    /// </summary>
    private void DrawBlurSettings()
    {
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
    }

    /// <summary>
    /// マウスカーソル下のポリゴンを特定します。
    /// </summary>
    /// <param name="e">現在のイベント。</param>
    private void UpdateHoverTriangle(Event e)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity) && hit.collider?.gameObject == m_Core.TargetRenderer.gameObject)
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
            Undo.RecordObject(this, "UVアイランド選択切替");
            m_Core.ToggleUVIslandSelection(m_HoverTriangle);
            m_IsolateMeshDirty = true;
            e.Use();
        }
        else if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && !e.alt)
        {
            if (m_HoverTriangle != m_LastProcessedTriangle)
            {
                Undo.RecordObject(this, "ペイント操作");
                if (m_Core.PaintMode) m_Core.AddTriangle(m_HoverTriangle);
                else m_Core.RemoveTriangle(m_HoverTriangle);

                m_LastProcessedTriangle = m_HoverTriangle;
                m_IsolateMeshDirty = true;
            }
            e.Use();
        }

        if (e.type == EventType.MouseUp && e.button == 0) m_LastProcessedTriangle = -1;
    }

    /// <summary>
    /// 2Dプレビュー上でのペイント入力を処理します。
    /// </summary>
    /// <param name="aspectRect">プレビューテクスチャの表示範囲。</param>
    private void Handle2DPaintInput(Rect aspectRect)
    {
        Event e = Event.current;
        if (!aspectRect.Contains(e.mousePosition))
        {
            if (e.type == EventType.MouseUp && e.button == 0) m_LastProcessedTriangle2D = -1;
            return;
        }

        if (e.type == EventType.MouseDown && e.shift && e.button == 0 && !e.alt)
        {
            Vector2 localPos = e.mousePosition - aspectRect.position;
            Vector2 uv = new Vector2(localPos.x / aspectRect.width, 1.0f - (localPos.y / aspectRect.height));
            int triIdx = m_Core.FindTriangleFromUV(uv);
            if (triIdx >= 0)
            {
                Undo.RecordObject(this, "UVアイランド選択切替 (2D)");
                m_Core.ToggleUVIslandSelection(triIdx);
                m_IsolateMeshDirty = true;
            }
            e.Use();
        }
        else if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && !e.alt)
        {
            Vector2 localPos = e.mousePosition - aspectRect.position;
            Vector2 uv = new Vector2(localPos.x / aspectRect.width, 1.0f - (localPos.y / aspectRect.height));

            int triIdx = m_Core.FindTriangleFromUV(uv);
            if (triIdx >= 0 && triIdx != m_LastProcessedTriangle2D)
            {
                Undo.RecordObject(this, "2Dペイント操作");
                if (m_Core.PaintMode) m_Core.AddTriangle(triIdx);
                else m_Core.RemoveTriangle(triIdx);

                m_LastProcessedTriangle2D = triIdx;
                m_IsolateMeshDirty = true;
            }
            e.Use();
        }

        if (e.type == EventType.MouseUp && e.button == 0) m_LastProcessedTriangle2D = -1;
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

        using (new Handles.DrawingScope(m_Core.TargetRenderer.transform.localToWorldMatrix))
        {
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            var verts = m_Core.Mesh.vertices;
            var tris = m_Core.Mesh.triangles;

            var linePoints = new List<Vector3>(m_Core.SelectedTriangles.Count * 6);

            foreach (var triIdx in m_Core.SelectedTriangles)
            {
                if (!m_Core.IsTriangleInActiveScope(triIdx)) continue;
                int i0 = tris[triIdx * 3 + 0], i1 = tris[triIdx * 3 + 1], i2 = tris[triIdx * 3 + 2];
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
                int i0 = tris[m_HoverTriangle * 3 + 0], i1 = tris[m_HoverTriangle * 3 + 1], i2 = tris[m_HoverTriangle * 3 + 2];
                Handles.DrawAAPolyLine(2f, verts[i0], verts[i1], verts[i2], verts[i0]);
            }
            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
            Handles.color = Color.white;
        }
    }

    /// <summary>
    /// 分離表示モードを設定します。
    /// </summary>
    /// <param name="enabled">有効にするかどうか。</param>
    private void SetIsolationMode(bool enabled)
    {
        m_Core.IsolateEnabled = enabled;
        if (m_Core.TargetRenderer != null)
        {
            m_Core.TargetRenderer.enabled = !enabled;
        }
        m_IsolateMeshDirty = true;
        SceneView.RepaintAll();
    }

    /// <summary>
    /// レンダラーが選択された際のコールバック処理です。
    /// </summary>
    /// <param name="selectedRenderer">選択されたレンダラー。</param>
    private void OnRendererSelected(Renderer selectedRenderer)
    {
        Undo.RecordObject(this, "ターゲット変更");
        SetIsolationMode(false);
        m_Core.SetTarget(selectedRenderer);
        CreatePreviewTexture();
        m_Core.UpdatePreviewTexture(m_SharpPreviewTex);
        m_IsolateMeshDirty = true;
        m_ShowRendererSelection = false;
    }

    /// <summary>
    /// プレビュー用のテクスチャを生成します。
    /// </summary>
    private Texture2D GenerateRendererPreview(Renderer renderer)
    {
        if (renderer == null) return null;

        Mesh mesh = null;
        if (renderer is SkinnedMeshRenderer smr)
        {
            mesh = new Mesh();
            smr.BakeMesh(mesh);
        }
        else if (renderer is MeshRenderer mr)
        {
            var mf = mr.GetComponent<MeshFilter>();
            if (mf != null) mesh = mf.sharedMesh;
        }

        if (mesh == null) return null;

        GameObject previewObject = null;
        RenderTexture renderTexture = null;
        Texture2D thumbnail = null;
        GameObject cameraGO = null;
        GameObject lightGO = null;

        try
        {
            int previewLayer = 31;
            renderTexture = RenderTexture.GetTemporary(RENDERER_PREVIEW_SIZE * 2, RENDERER_PREVIEW_SIZE * 2, 24, RenderTextureFormat.ARGB32);
            renderTexture.antiAliasing = 4;

            cameraGO = new GameObject("PreviewCamera") { hideFlags = HideFlags.HideAndDontSave };
            var previewCamera = cameraGO.AddComponent<Camera>();
            previewCamera.targetTexture = renderTexture;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0f);
            previewCamera.fieldOfView = 30f;
            previewCamera.cullingMask = 1 << previewLayer;

            previewObject = new GameObject("PreviewMesh") { hideFlags = HideFlags.HideAndDontSave };
            previewObject.layer = previewLayer;

            var meshFilter = previewObject.AddComponent<MeshFilter>();
            var meshRenderer = previewObject.AddComponent<MeshRenderer>();
            meshFilter.sharedMesh = mesh;
            meshRenderer.sharedMaterials = renderer.sharedMaterials;

            Bounds bounds = mesh.bounds;
            float distance = (bounds.extents.magnitude * 1.5f) / Mathf.Tan(previewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            distance *= 1.5f; // Add padding

            Vector3 cameraDirection = new Vector3(1f, 0.7f, 1f).normalized;

            cameraGO.transform.position = bounds.center + cameraDirection * distance;
            cameraGO.transform.LookAt(bounds.center);

            lightGO = new GameObject("PreviewLight") { hideFlags = HideFlags.HideAndDontSave };
            lightGO.transform.rotation = cameraGO.transform.rotation;
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.0f;
            light.color = Color.white;

            previewCamera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTexture;

            thumbnail = new Texture2D(RENDERER_PREVIEW_SIZE, RENDERER_PREVIEW_SIZE, TextureFormat.RGBA32, false);
            thumbnail.ReadPixels(new Rect((RENDERER_PREVIEW_SIZE * 2 - RENDERER_PREVIEW_SIZE) / 2, (RENDERER_PREVIEW_SIZE * 2 - RENDERER_PREVIEW_SIZE) / 2, RENDERER_PREVIEW_SIZE, RENDERER_PREVIEW_SIZE), 0, 0);
            thumbnail.Apply();

            RenderTexture.active = previous;
        }
        finally
        {
            if (cameraGO != null) DestroyImmediate(cameraGO);
            if (lightGO != null) DestroyImmediate(lightGO);
            if (previewObject != null) DestroyImmediate(previewObject);
            if (renderTexture != null) RenderTexture.ReleaseTemporary(renderTexture);
            if (renderer is SkinnedMeshRenderer) DestroyImmediate(mesh);
        }

        return thumbnail;
    }

    /// <summary>
    /// プレビュー用のテクスチャキャッシュをクリアします。
    /// </summary>
    private void ClearPreviewTextures()
    {
        foreach (var tex in m_PreviewTextures.Values)
        {
            if (tex != null)
            {
                DestroyImmediate(tex);
            }
        }
        m_PreviewTextures.Clear();
    }
}
