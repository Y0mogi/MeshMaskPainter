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
    private bool m_Is2DPaintEnabled = true;

    private Vector2 m_ScrollPos;
    private Vector2 m_RendererListScrollPos;

    private GameObject m_TargetGameObject;
    private List<Renderer> m_FoundRenderers = new List<Renderer>();
    private readonly Dictionary<Renderer, Texture2D> m_PreviewTextures = new Dictionary<Renderer, Texture2D>();
    private bool m_ShowRendererSelection = false;
    private const int RENDERER_PREVIEW_SIZE = 64;

    private Vector2 m_UvPreviewPan = Vector2.zero;
    private float m_UvPreviewZoom = 1.0f;
    private const float MIN_ZOOM = 0.2f;
    private const float MAX_ZOOM = 50.0f;

    private static class Tooltips
    {
        public static readonly GUIContent TargetObject = new GUIContent("ターゲットオブジェクト", "マスクをペイントしたいGameObjectをここにドラッグ＆ドロップします。");
        public static readonly GUIContent RebuildMeshInfo = new GUIContent("メッシュ情報を再構築", "メッシュが変更された場合や、表示がおかしい場合にクリックして、メッシュの内部データを再計算します。");
        public static readonly GUIContent ClearSelection = new GUIContent("クリア", "現在選択されているポリゴンをすべて解除します。");
        public static readonly GUIContent InvertSelection = new GUIContent("反転", "選択範囲を反転します。選択されていないポリゴンが選択され、選択中のポリゴンは解除されます。");
        public static readonly GUIContent GrowSelection = new GUIContent("選択を拡張 (Grow)", "現在の選択範囲を、隣接するポリゴンに1段階広げます。");
        public static readonly GUIContent ShrinkSelection = new GUIContent("選択を縮小 (Shrink)", "現在の選択範囲の境界を1段階狭めます。");
        public static readonly GUIContent SaveSelection = new GUIContent("選択を保存", "現在の選択範囲をファイル(.mmp)に保存します。");
        public static readonly GUIContent LoadSelection = new GUIContent("選択を読み込み", "ファイル(.mmp)から選択範囲を読み込みます。");
        public static readonly GUIContent OnlyActiveSubmesh = new GUIContent("アクティブなサブメッシュのみ", "チェックを入れると、ドロップダウンで選択したサブメッシュにのみペイントや選択操作が影響するようになります。");
        public static readonly GUIContent ShowWireframe = new GUIContent("選択箇所のワイヤーフレーム表示", "シーンビューで、選択中のポリゴンをワイヤーフレームで表示します。");
        public static readonly GUIContent HoverHighlight = new GUIContent("ホバー箇所をハイライト", "シーンビューで、マウスカーソルが乗っているポリゴンをハイライト表示します。");
        public static readonly GUIContent IsolateSelection = new GUIContent("選択箇所のみ分離表示", "シーンビューで、選択中のポリゴンのみを表示します。モデルの裏側などを確認するのに便利です。");
        public static readonly GUIContent BaseTexture = new GUIContent("ベーステクスチャ (参照用)", "UVプレビューの背景や、テクスチャペイントの元として使用するテクスチャを指定します。");
        public static readonly GUIContent ShowBaseTextureInPreview = new GUIContent("プレビューにベーステクスチャを表示", "チェックを入れると、UVプレビューの背景に上記で指定したベーステクスチャが表示されます。");
        public static readonly GUIContent AddTemporaryCollider = new GUIContent("一時的なMeshColliderを自動追加", "ペイント対象のオブジェクトにColliderがない場合、レイキャストのために一時的なMeshColliderを自動で追加します。通常はオンのままで問題ありません。");
        public static readonly GUIContent ResetView = new GUIContent("ビューをリセット", "UVプレビューの拡大率と位置をリセットします。");
        public static readonly GUIContent BlurPreview = new GUIContent("ぼかしプレビュー", "現在のぼかし設定をプレビューに適用します。");
        public static readonly GUIContent NormalPreview = new GUIContent("通常プレビュー", "ぼかしを解除した通常のプレビューに戻ります。");
    }

    [MenuItem("Tools/Mesh Mask Painter")]
    public static void ShowWindow()
    {
        var w = GetWindow<MeshMaskPainterWindow>("MMP");
        w.minSize = new Vector2(800, 900);
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        Undo.undoRedoPerformed += OnUndoRedo;
        m_Core.UndoRedoPerformed += OnUndoRedo;
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
    }

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
    private void OnUndoRedo()
    {
        // ペイントモードの場合、Undo/Redoで変更された選択範囲をテクスチャに再反映させる
        if (m_Core.CurrentTool == ToolMode.Paint)
        {
            m_Core.UpdateTextureFromSelection();
        }

        if (m_SharpPreviewTex != null)
        {
            m_Core.UpdatePreviewTexture(m_SharpPreviewTex);
        }
        m_IsolateMeshDirty = true;
        Repaint();
        SceneView.RepaintAll();

    }

    private void OnHierarchyChanged()
    {
        if (m_Core.TargetRenderer == null)
        {
            m_Core.SetTarget(null);
            Repaint();
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.BeginHorizontal();
        DrawPreviewPanel();
        DrawControlPanel();
        EditorGUILayout.EndHorizontal();
    }

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
        HandleSelectionInput(e);
        if (m_Core.ShowWireframe)
        {
            DrawSelectionOverlays();
        }
        view.Repaint();
    }

    private void DrawPreviewPanel()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUILayout.LabelField("UVプレビュー", GUILayout.Width(100));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(Tooltips.ResetView, EditorStyles.toolbarButton, GUILayout.Width(100)))
                {
                    m_UvPreviewPan = Vector2.zero;
                    m_UvPreviewZoom = 1.0f;
                }
            }
            using (new EditorGUILayout.VerticalScope("box", GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                Rect rect = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                Texture2D currentPreview = m_IsPreviewBlurred ? m_BlurredPreviewTex : m_SharpPreviewTex;
                if (currentPreview != null)
                {
                    Rect aspectRect = MeshMaskPainterCore.GUITool.GetAspectRect(rect, (float)currentPreview.width / currentPreview.height);
                    if (Event.current != null && EditorWindow.mouseOverWindow == this)
                    {
                        HandleUvPreviewControls(aspectRect);
                        if (m_Is2DPaintEnabled)
                        {
                            Handle2DSelectionInput(aspectRect);
                        }
                    }
                    EditorGUI.DrawRect(aspectRect, Color.black);
                    float texCoordHeight = 1f / m_UvPreviewZoom;
                    float texCoordY = 1f - m_UvPreviewPan.y - texCoordHeight;
                    Rect texCoords = new Rect(m_UvPreviewPan.x, texCoordY, 1f / m_UvPreviewZoom, texCoordHeight);
                    GUI.DrawTextureWithTexCoords(aspectRect, currentPreview, texCoords, true);
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
                        if (GUILayout.Button(Tooltips.BlurPreview, EditorStyles.toolbarButton)) { ApplyBlurToPreview(); m_IsPreviewBlurred = true; }
                    }
                    else
                    {
                        if (GUILayout.Button(Tooltips.NormalPreview, EditorStyles.toolbarButton)) { m_IsPreviewBlurred = false; }
                    }
                }
            }
        }
    }

    private void DrawControlPanel()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(380)))
        {
            m_ScrollPos = EditorGUILayout.BeginScrollView(m_ScrollPos);
            EditorGUILayout.LabelField("アバター選択", EditorStyles.boldLabel);
            DrawTargetSettings();
            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("ツール", EditorStyles.boldLabel);
            DrawToolPanel();
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("操作 & 表示設定", EditorStyles.boldLabel);
            DrawPaintAndDisplaySettings();
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("エクスポート", EditorStyles.boldLabel);
            DrawExportSettings();
            EditorGUILayout.EndScrollView();
        }
    }

    private void DrawTargetSettings()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUI.BeginChangeCheck();
            m_TargetGameObject = (GameObject)EditorGUILayout.ObjectField(Tooltips.TargetObject, m_TargetGameObject, typeof(GameObject), true);
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
                EditorGUILayout.HelpBox("使用するアバターを選択してください。", MessageType.Info);
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
                if (GUILayout.Button(Tooltips.RebuildMeshInfo))
                {
                    Undo.RecordObject(this, "マップを再構築");
                    m_Core.SetupMeshAndMappings();
                    m_Core.UpdatePreviewTexture(m_SharpPreviewTex);
                    m_IsolateMeshDirty = true;
                }
            }
        }
    }

    private void DrawToolPanel()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            m_Core.CurrentTool = (ToolMode)GUILayout.Toolbar((int)m_Core.CurrentTool, new[] { "マスク選択", "テクスチャペイント" });
            EditorGUILayout.Space(5);

            if (m_Core.CurrentTool == ToolMode.Paint)
            {
                // 色の変更を検知して、即座にプレビューに反映させる
                EditorGUI.BeginChangeCheck();
                m_Core.PaintColor = EditorGUILayout.ColorField("ペイント色", m_Core.PaintColor);
                if (EditorGUI.EndChangeCheck())
                {
                    // 選択範囲はそのままに、テクスチャだけを新しい色で更新
                    m_Core.UpdateTextureFromSelection();
                    m_Core.UpdatePreviewTexture(m_SharpPreviewTex);
                }
            }
            EditorGUILayout.Space(5);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(Tooltips.ClearSelection)) { Undo.RecordObject(this, "選択をクリア"); m_Core.ClearSelection(); }
                using (new EditorGUI.DisabledScope(m_Core.SelectedTriangles.Count == 0)) { if (GUILayout.Button(Tooltips.InvertSelection)) { Undo.RecordObject(this, "選択を反転"); m_Core.InvertSelection(); } }
            }
            using (new EditorGUI.DisabledScope(m_Core.SelectedTriangles.Count == 0 || !m_Core.IsAdjacencyMapReady()))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(Tooltips.GrowSelection)) { Undo.RecordObject(this, "選択を拡張"); m_Core.GrowSelection(); }
                    if (GUILayout.Button(Tooltips.ShrinkSelection)) { Undo.RecordObject(this, "選択を縮小"); m_Core.ShrinkSelection(); }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(Tooltips.SaveSelection)) { m_Core.SaveSelection(); }
                if (GUILayout.Button(Tooltips.LoadSelection)) { Undo.RecordObject(this, "選択を読み込み"); m_Core.LoadSelection(); }
            }
        }
    }

    private void DrawPaintAndDisplaySettings()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.HelpBox("クリックで選択、Shift+クリックでUVアイランド選択切替。\nCtrlキーを押しながらクリックで選択解除します。", MessageType.Info);
            EditorGUILayout.Space(5);

            if (m_Core.CurrentTool == ToolMode.Paint)
            {
                if (m_Core.BaseTexture == null)
                {
                    EditorGUILayout.HelpBox("まず「ベーステクスチャ(参照用)」にペイントの元となるテクスチャを設定してください。", MessageType.Warning);
                }
                else if (m_Core.m_PaintableTexture == null)
                {
                    EditorGUILayout.HelpBox("下のボタンを押して、ペイントの準備をしてください。", MessageType.Info);
                    if (GUILayout.Button("ペイント用テクスチャを準備"))
                    {
                        Undo.RecordObject(this, "ペイント用テクスチャを準備");
                        m_Core.CreatePaintableTexture();
                    }
                }
                else
                {
                    if (GUILayout.Button("ペイントしたテクスチャを保存..."))
                    {
                        m_Core.SavePaintableTexture();
                    }
                }
            }
            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                m_Core.OnlyActiveSubmesh = EditorGUILayout.ToggleLeft(Tooltips.OnlyActiveSubmesh, m_Core.OnlyActiveSubmesh, GUILayout.Width(180));
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

            m_Core.ShowWireframe = EditorGUILayout.ToggleLeft(Tooltips.ShowWireframe, m_Core.ShowWireframe);
            using (new EditorGUI.DisabledScope(!m_Core.ShowWireframe))
            {
                EditorGUI.indentLevel++;
                m_Core.CullBackfaceWireframe = EditorGUILayout.ToggleLeft("カメラから見えている面のみ表示", m_Core.CullBackfaceWireframe);
                EditorGUI.indentLevel--;
            }
            m_Core.HoverHighlight = EditorGUILayout.ToggleLeft(Tooltips.HoverHighlight, m_Core.HoverHighlight);
            bool newIsolate = EditorGUILayout.ToggleLeft(Tooltips.IsolateSelection, m_Core.IsolateEnabled);
            if (newIsolate != m_Core.IsolateEnabled)
            {
                SetIsolationMode(newIsolate);
            }
            EditorGUILayout.Space(5);
            m_Core.EdgeSelectionColor = EditorGUILayout.ColorField("選択ワイヤーフレームの色", m_Core.EdgeSelectionColor);
            m_Core.EdgeHighlightColor = EditorGUILayout.ColorField("ハイライトの色", m_Core.EdgeHighlightColor);
            EditorGUILayout.Space(5);

            using (new EditorGUI.DisabledScope(m_Core.TargetRenderer == null))
            {
                if (m_Core.BaseTexture == null && m_Core.TargetRenderer != null && m_Core.TargetRenderer.sharedMaterial != null)
                {
                    m_Core.BaseTexture = m_Core.TargetRenderer.sharedMaterial.mainTexture as Texture2D;
                }
                m_Core.BaseTexture = (Texture2D)EditorGUILayout.ObjectField(Tooltips.BaseTexture, m_Core.BaseTexture, typeof(Texture2D), false, GUILayout.Height(20));

                bool newShowBaseTexture = EditorGUILayout.ToggleLeft(Tooltips.ShowBaseTextureInPreview, m_Core.ShowBaseTextureInPreview);
                if (newShowBaseTexture != m_Core.ShowBaseTextureInPreview) { m_Core.ShowBaseTextureInPreview = newShowBaseTexture; m_Core.UpdatePreviewTexture(m_SharpPreviewTex); }

                if (m_Core.ShowBaseTextureInPreview && m_Core.BaseTexture != null && !m_Core.BaseTexture.isReadable && m_Core.m_PaintableTexture == null)
                {
                    EditorGUILayout.HelpBox("プレビュー表示のために、ベーステクスチャの Read/Write を有効にしてください。", MessageType.Warning);
                }
            }
            EditorGUILayout.Space(5);

            m_Core.AddTemporaryCollider = EditorGUILayout.ToggleLeft(Tooltips.AddTemporaryCollider, m_Core.AddTemporaryCollider);
        }
    }

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

    private void DrawExportMaskTextureUI()
    {
        EditorGUILayout.HelpBox("選択範囲を新規マスクテクスチャとして出力します。", MessageType.Info);
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

    private void HandleSelectionInput(Event e)
    {
        if (m_HoverTriangle < 0)
        {
            if (e.type == EventType.MouseUp && e.button == 0) m_LastProcessedTriangle = -1;
            return;
        }

        if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && !e.alt)
        {
            // 同じトライアングル上でドラッグしても処理が重複しないようにする
            if (m_HoverTriangle != m_LastProcessedTriangle)
            {
                Undo.RecordObject(this, "ポリゴン選択操作");
                if (m_Core.CurrentTool == ToolMode.Paint && m_Core.m_PaintableTexture != null)
                {
                    Undo.RegisterCompleteObjectUndo(m_Core.m_PaintableTexture, "テクスチャペイント");
                }

                if (e.shift) { m_Core.ToggleUVIslandSelection(m_HoverTriangle); }
                else if (e.control) { m_Core.RemoveTriangle(m_HoverTriangle); }
                else { m_Core.AddTriangle(m_HoverTriangle); }

                m_LastProcessedTriangle = m_HoverTriangle;
                m_IsolateMeshDirty = true;
            }
            e.Use();
        }

        if (e.type == EventType.MouseUp && e.button == 0)
        {
            m_LastProcessedTriangle = -1;
        }

    }

    private void HandleUvPreviewControls(Rect controlRect)
    {
        Event e = Event.current;
        if (!controlRect.Contains(e.mousePosition)) return;
        if (e.type == EventType.ScrollWheel)
        {
            float zoomDelta = -e.delta.y * 0.05f;
            float oldZoom = m_UvPreviewZoom;
            m_UvPreviewZoom = Mathf.Clamp(m_UvPreviewZoom + zoomDelta * m_UvPreviewZoom, MIN_ZOOM, MAX_ZOOM);
            Vector2 mousePosInRect = e.mousePosition - controlRect.position;
            Vector2 uvBeforeZoom = new Vector2(m_UvPreviewPan.x + (mousePosInRect.x / controlRect.width) / oldZoom, m_UvPreviewPan.y + (mousePosInRect.y / controlRect.height) / oldZoom);
            m_UvPreviewPan = new Vector2(uvBeforeZoom.x - (mousePosInRect.x / controlRect.width) / m_UvPreviewZoom, uvBeforeZoom.y - (mousePosInRect.y / controlRect.height) / m_UvPreviewZoom);
            e.Use();
        }
        if (e.type == EventType.MouseDrag && (e.button == 2 || (e.alt && e.button == 0)))
        {
            Vector2 panDelta = e.delta;
            panDelta.x /= controlRect.width * m_UvPreviewZoom;
            panDelta.y /= controlRect.height * m_UvPreviewZoom;
            m_UvPreviewPan -= panDelta;
            e.Use();
        }
        float maxPan = 1f - (1f / m_UvPreviewZoom);
        m_UvPreviewPan.x = Mathf.Clamp(m_UvPreviewPan.x, 0, maxPan);
        m_UvPreviewPan.y = Mathf.Clamp(m_UvPreviewPan.y, 0, maxPan);
        if (m_UvPreviewZoom <= 1.0f) m_UvPreviewPan = Vector2.zero;
    }

    private void Handle2DSelectionInput(Rect aspectRect)
    {
        Event e = Event.current;
        if (!aspectRect.Contains(e.mousePosition))
        {
            if (e.type == EventType.MouseUp && e.button == 0) m_LastProcessedTriangle2D = -1;
            return;
        }
        Vector2 localPos = e.mousePosition - aspectRect.position;
        Vector2 uvInView = new Vector2(localPos.x / aspectRect.width, localPos.y / aspectRect.height);
        Vector2 uv = new Vector2(m_UvPreviewPan.x + uvInView.x / m_UvPreviewZoom, (1.0f - m_UvPreviewPan.y) - uvInView.y / m_UvPreviewZoom);
        int triIdx = m_Core.FindTriangleFromUV(uv);
        if (triIdx < 0) return;

        if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && !e.alt)
        {
            if (triIdx != m_LastProcessedTriangle2D)
            {
                Undo.RecordObject(this, "ポリゴン選択操作(2D)");
                if (m_Core.CurrentTool == ToolMode.Paint && m_Core.m_PaintableTexture != null)
                {
                    Undo.RegisterCompleteObjectUndo(m_Core.m_PaintableTexture, "テクスチャペイント(2D)");
                }

                if (e.shift) { m_Core.ToggleUVIslandSelection(triIdx); }
                else if (e.control) { m_Core.RemoveTriangle(triIdx); }
                else { m_Core.AddTriangle(triIdx); }

                m_LastProcessedTriangle2D = triIdx;
                m_IsolateMeshDirty = true;
            }
            e.Use();
        }

        if (e.type == EventType.MouseUp && e.button == 0)
        {
            m_LastProcessedTriangle2D = -1;
        }

    }

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

    private void ApplyBlurToPreview()
    {
        if (m_SharpPreviewTex == null || m_BlurredPreviewTex == null) return;
        var pixels = m_SharpPreviewTex.GetPixels32();
        MeshAnalysis.ApplyFastBlur(pixels, m_SharpPreviewTex.width, m_SharpPreviewTex.height, m_Core.BlurRadius, m_Core.BlurIterations);
        m_BlurredPreviewTex.SetPixels32(pixels);
        m_BlurredPreviewTex.Apply(false);
    }

    private void DrawSelectionOverlays()
    {
        if (!m_Core.IsReady()) return;
        // シーンビューのカメラを取得
        var sceneCam = SceneView.currentDrawingSceneView.camera;
        if (sceneCam == null) return;

        // ターゲットオブジェクトのTransformを取得
        Transform targetTransform = m_Core.TargetRenderer.transform;

        using (new Handles.DrawingScope(targetTransform.localToWorldMatrix))
        {
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            var verts = m_Core.Mesh.vertices;
            var tris = m_Core.Mesh.triangles;

            var linePoints = new List<Vector3>(m_Core.SelectedTriangles.Count * 6);

            // カメラの視線ベクトルをオブジェクトのローカル空間に変換
            Vector3 camForwardLocal = targetTransform.InverseTransformDirection(sceneCam.transform.forward);

            foreach (var triIdx in m_Core.SelectedTriangles)
            {
                if (!m_Core.IsTriangleInActiveScope(triIdx)) continue;

                int i0 = tris[triIdx * 3 + 0];
                int i1 = tris[triIdx * 3 + 1];
                int i2 = tris[triIdx * 3 + 2];

                Vector3 v0 = verts[i0];
                Vector3 v1 = verts[i1];
                Vector3 v2 = verts[i2];

                if (m_Core.CullBackfaceWireframe)
                {
                    // ポリゴンの法線を計算
                    Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;

                    // 法線とカメラの視線ベクトルの内積を計算
                    // 内積が0より大きい場合、ポリゴンはカメラから見て裏側を向いている
                    if (Vector3.Dot(normal, camForwardLocal) > 0)
                    {
                        continue; // このポリゴンは描画しない
                    }
                }

                linePoints.Add(v0); linePoints.Add(v1);
                linePoints.Add(v1); linePoints.Add(v2);
                linePoints.Add(v2); linePoints.Add(v0);
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
                Handles.DrawAAPolyLine(2f, verts[i0], verts[i1], verts[i2], verts[i0]);
            }
            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
            Handles.color = Color.white;
        }

    }

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

    private void OnRendererSelected(Renderer selectedRenderer)
    {
        Undo.RecordObject(this, "ターゲット変更");
        SetIsolationMode(false);
        m_Core.SetTarget(selectedRenderer);
        if (m_Core.TargetRenderer != null && m_Core.TargetRenderer.sharedMaterial != null)
        {
            m_Core.BaseTexture = m_Core.TargetRenderer.sharedMaterial.mainTexture as Texture2D;
        }
        else
        {
            m_Core.BaseTexture = null;
        }
        CreatePreviewTexture();
        m_Core.UpdatePreviewTexture(m_SharpPreviewTex);
        m_IsolateMeshDirty = true;
        m_ShowRendererSelection = false;
    }

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
            distance *= 1.5f;
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