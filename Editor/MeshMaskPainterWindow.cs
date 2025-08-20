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
        public static readonly GUIContent TargetObject = new GUIContent("�^�[�Q�b�g�I�u�W�F�N�g", "�}�X�N���y�C���g������GameObject�������Ƀh���b�O���h���b�v���܂��B");
        public static readonly GUIContent RebuildMeshInfo = new GUIContent("���b�V�������č\�z", "���b�V�����ύX���ꂽ�ꍇ��A�\�������������ꍇ�ɃN���b�N���āA���b�V���̓����f�[�^���Čv�Z���܂��B");
        public static readonly GUIContent ClearSelection = new GUIContent("�N���A", "���ݑI������Ă���|���S�������ׂĉ������܂��B");
        public static readonly GUIContent InvertSelection = new GUIContent("���]", "�I��͈͂𔽓]���܂��B�I������Ă��Ȃ��|���S�����I������A�I�𒆂̃|���S���͉�������܂��B");
        public static readonly GUIContent GrowSelection = new GUIContent("�I�����g�� (Grow)", "���݂̑I��͈͂��A�אڂ���|���S����1�i�K�L���܂��B");
        public static readonly GUIContent ShrinkSelection = new GUIContent("�I�����k�� (Shrink)", "���݂̑I��͈͂̋��E��1�i�K���߂܂��B");
        public static readonly GUIContent SaveSelection = new GUIContent("�I����ۑ�", "���݂̑I��͈͂��t�@�C��(.mmp)�ɕۑ����܂��B");
        public static readonly GUIContent LoadSelection = new GUIContent("�I����ǂݍ���", "�t�@�C��(.mmp)����I��͈͂�ǂݍ��݂܂��B");
        public static readonly GUIContent OnlyActiveSubmesh = new GUIContent("�A�N�e�B�u�ȃT�u���b�V���̂�", "�`�F�b�N������ƁA�h���b�v�_�E���őI�������T�u���b�V���ɂ̂݃y�C���g��I�𑀍삪�e������悤�ɂȂ�܂��B");
        public static readonly GUIContent ShowWireframe = new GUIContent("�I���ӏ��̃��C���[�t���[���\��", "�V�[���r���[�ŁA�I�𒆂̃|���S�������C���[�t���[���ŕ\�����܂��B");
        public static readonly GUIContent HoverHighlight = new GUIContent("�z�o�[�ӏ����n�C���C�g", "�V�[���r���[�ŁA�}�E�X�J�[�\��������Ă���|���S�����n�C���C�g�\�����܂��B");
        public static readonly GUIContent IsolateSelection = new GUIContent("�I���ӏ��̂ݕ����\��", "�V�[���r���[�ŁA�I�𒆂̃|���S���݂̂�\�����܂��B���f���̗����Ȃǂ��m�F����̂ɕ֗��ł��B");
        public static readonly GUIContent BaseTexture = new GUIContent("�x�[�X�e�N�X�`�� (�Q�Ɨp)", "UV�v���r���[�̔w�i��A�e�N�X�`���y�C���g�̌��Ƃ��Ďg�p����e�N�X�`�����w�肵�܂��B");
        public static readonly GUIContent ShowBaseTextureInPreview = new GUIContent("�v���r���[�Ƀx�[�X�e�N�X�`����\��", "�`�F�b�N������ƁAUV�v���r���[�̔w�i�ɏ�L�Ŏw�肵���x�[�X�e�N�X�`�����\������܂��B");
        public static readonly GUIContent AddTemporaryCollider = new GUIContent("�ꎞ�I��MeshCollider�������ǉ�", "�y�C���g�Ώۂ̃I�u�W�F�N�g��Collider���Ȃ��ꍇ�A���C�L���X�g�̂��߂Ɉꎞ�I��MeshCollider�������Œǉ����܂��B�ʏ�̓I���̂܂܂Ŗ�肠��܂���B");
        public static readonly GUIContent ResetView = new GUIContent("�r���[�����Z�b�g", "UV�v���r���[�̊g�嗦�ƈʒu�����Z�b�g���܂��B");
        public static readonly GUIContent BlurPreview = new GUIContent("�ڂ����v���r���[", "���݂̂ڂ����ݒ���v���r���[�ɓK�p���܂��B");
        public static readonly GUIContent NormalPreview = new GUIContent("�ʏ�v���r���[", "�ڂ��������������ʏ�̃v���r���[�ɖ߂�܂��B");
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
        // �y�C���g���[�h�̏ꍇ�AUndo/Redo�ŕύX���ꂽ�I��͈͂��e�N�X�`���ɍĔ��f������
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
                EditorGUILayout.LabelField("UV�v���r���[", GUILayout.Width(100));
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
            EditorGUILayout.LabelField("�A�o�^�[�I��", EditorStyles.boldLabel);
            DrawTargetSettings();
            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("�c�[��", EditorStyles.boldLabel);
            DrawToolPanel();
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("���� & �\���ݒ�", EditorStyles.boldLabel);
            DrawPaintAndDisplaySettings();
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("�G�N�X�|�[�g", EditorStyles.boldLabel);
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
                EditorGUILayout.LabelField("���݂̃^�[�Q�b�g:", m_Core.TargetRenderer.gameObject.name);
            }
            else if (m_TargetGameObject != null)
            {
                EditorGUILayout.HelpBox("�g�p����A�o�^�[��I�����Ă��������B", MessageType.Info);
            }
            if (m_TargetGameObject != null)
            {
                m_ShowRendererSelection = EditorGUILayout.Foldout(m_ShowRendererSelection, $"�������������_���[ ({m_FoundRenderers.Count})", true);
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
                                    if (GUILayout.Button("���̃����_���[��I��"))
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
                    Undo.RecordObject(this, "�}�b�v���č\�z");
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
            m_Core.CurrentTool = (ToolMode)GUILayout.Toolbar((int)m_Core.CurrentTool, new[] { "�}�X�N�I��", "�e�N�X�`���y�C���g" });
            EditorGUILayout.Space(5);

            if (m_Core.CurrentTool == ToolMode.Paint)
            {
                // �F�̕ύX�����m���āA�����Ƀv���r���[�ɔ��f������
                EditorGUI.BeginChangeCheck();
                m_Core.PaintColor = EditorGUILayout.ColorField("�y�C���g�F", m_Core.PaintColor);
                if (EditorGUI.EndChangeCheck())
                {
                    // �I��͈͂͂��̂܂܂ɁA�e�N�X�`��������V�����F�ōX�V
                    m_Core.UpdateTextureFromSelection();
                    m_Core.UpdatePreviewTexture(m_SharpPreviewTex);
                }
            }
            EditorGUILayout.Space(5);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(Tooltips.ClearSelection)) { Undo.RecordObject(this, "�I�����N���A"); m_Core.ClearSelection(); }
                using (new EditorGUI.DisabledScope(m_Core.SelectedTriangles.Count == 0)) { if (GUILayout.Button(Tooltips.InvertSelection)) { Undo.RecordObject(this, "�I���𔽓]"); m_Core.InvertSelection(); } }
            }
            using (new EditorGUI.DisabledScope(m_Core.SelectedTriangles.Count == 0 || !m_Core.IsAdjacencyMapReady()))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(Tooltips.GrowSelection)) { Undo.RecordObject(this, "�I�����g��"); m_Core.GrowSelection(); }
                    if (GUILayout.Button(Tooltips.ShrinkSelection)) { Undo.RecordObject(this, "�I�����k��"); m_Core.ShrinkSelection(); }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(Tooltips.SaveSelection)) { m_Core.SaveSelection(); }
                if (GUILayout.Button(Tooltips.LoadSelection)) { Undo.RecordObject(this, "�I����ǂݍ���"); m_Core.LoadSelection(); }
            }
        }
    }

    private void DrawPaintAndDisplaySettings()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.HelpBox("�N���b�N�őI���AShift+�N���b�N��UV�A�C�����h�I��ؑցB\nCtrl�L�[�������Ȃ���N���b�N�őI���������܂��B", MessageType.Info);
            EditorGUILayout.Space(5);

            if (m_Core.CurrentTool == ToolMode.Paint)
            {
                if (m_Core.BaseTexture == null)
                {
                    EditorGUILayout.HelpBox("�܂��u�x�[�X�e�N�X�`��(�Q�Ɨp)�v�Ƀy�C���g�̌��ƂȂ�e�N�X�`����ݒ肵�Ă��������B", MessageType.Warning);
                }
                else if (m_Core.m_PaintableTexture == null)
                {
                    EditorGUILayout.HelpBox("���̃{�^���������āA�y�C���g�̏��������Ă��������B", MessageType.Info);
                    if (GUILayout.Button("�y�C���g�p�e�N�X�`��������"))
                    {
                        Undo.RecordObject(this, "�y�C���g�p�e�N�X�`��������");
                        m_Core.CreatePaintableTexture();
                    }
                }
                else
                {
                    if (GUILayout.Button("�y�C���g�����e�N�X�`����ۑ�..."))
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
                m_Core.CullBackfaceWireframe = EditorGUILayout.ToggleLeft("�J�������猩���Ă���ʂ̂ݕ\��", m_Core.CullBackfaceWireframe);
                EditorGUI.indentLevel--;
            }
            m_Core.HoverHighlight = EditorGUILayout.ToggleLeft(Tooltips.HoverHighlight, m_Core.HoverHighlight);
            bool newIsolate = EditorGUILayout.ToggleLeft(Tooltips.IsolateSelection, m_Core.IsolateEnabled);
            if (newIsolate != m_Core.IsolateEnabled)
            {
                SetIsolationMode(newIsolate);
            }
            EditorGUILayout.Space(5);
            m_Core.EdgeSelectionColor = EditorGUILayout.ColorField("�I�����C���[�t���[���̐F", m_Core.EdgeSelectionColor);
            m_Core.EdgeHighlightColor = EditorGUILayout.ColorField("�n�C���C�g�̐F", m_Core.EdgeHighlightColor);
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
                    EditorGUILayout.HelpBox("�v���r���[�\���̂��߂ɁA�x�[�X�e�N�X�`���� Read/Write ��L���ɂ��Ă��������B", MessageType.Warning);
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
            m_ExportTab = GUILayout.Toolbar(m_ExportTab, new[] { "�}�X�N�o��", "���_�J���[", "�e�N�X�`������" });
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
        EditorGUILayout.HelpBox("�I��͈͂�V�K�}�X�N�e�N�X�`���Ƃ��ďo�͂��܂��B", MessageType.Info);
        EditorGUILayout.Space(5);
        m_Core.ExportUseBaseTextureSize = EditorGUILayout.ToggleLeft("�x�[�X�e�N�X�`���Ɠ��𑜓x�ŏo��", m_Core.ExportUseBaseTextureSize);
        using (new EditorGUI.DisabledScope(m_Core.ExportUseBaseTextureSize))
        {
            m_Core.ExportWidth = EditorGUILayout.IntPopup("�o�͉𑜓x (��)", m_Core.ExportWidth, new[] { "1024", "2048", "4096" }, new[] { 1024, 2048, 4096 });
            m_Core.ExportHeight = EditorGUILayout.IntPopup("�o�͉𑜓x (����)", m_Core.ExportHeight, new[] { "1024", "2048", "4096" }, new[] { 1024, 2048, 4096 });
        }
        m_Core.ExportAlphaZeroBackground = EditorGUILayout.ToggleLeft("�w�i�𓧉� (�A���t�@=0)", m_Core.ExportAlphaZeroBackground);
        DrawBlurSettings();
        EditorGUILayout.BeginHorizontal();
        m_Core.ExportFolder = EditorGUILayout.TextField("�f�t�H���g�̏o�͐�", m_Core.ExportFolder);
        if (GUILayout.Button("�c", GUILayout.Width(28)))
        {
            string chosen = EditorUtility.OpenFolderPanel("�f�t�H���g�̏o�͐��I��", Application.dataPath, "");
            if (!string.IsNullOrEmpty(chosen))
            {
                if (chosen.StartsWith(Application.dataPath)) m_Core.ExportFolder = "Assets" + chosen.Substring(Application.dataPath.Length);
                else m_Core.ExportFolder = chosen;
            }
        }
        EditorGUILayout.EndHorizontal();
        using (new EditorGUI.DisabledScope(!m_Core.IsReady()))
        {
            if (GUILayout.Button("�}�X�N���o�� (���݂̑Ώ�)")) m_Core.ExportMaskTextures();
            if (GUILayout.Button("�S�T�u���b�V�����ʂɏo��")) m_Core.ExportMaskTextures(splitPerSubmesh: true);
        }
    }

    private void DrawBakeVertexColorUI()
    {
        EditorGUILayout.HelpBox("�I��͈͂𒸓_�J���[�ɏ������݁A�V�������b�V���A�Z�b�g���쐬���܂��B", MessageType.Info);
        m_Core.TargetVertexColorChannel = (TargetChannel)EditorGUILayout.EnumFlagsField("�������ݐ�`�����l��", m_Core.TargetVertexColorChannel);
        EditorGUILayout.Space(5);
        using (new EditorGUI.DisabledScope(!m_Core.IsReady() || m_Core.TargetRenderer.GetComponent<MeshFilter>() == null && m_Core.TargetRenderer.GetComponent<SkinnedMeshRenderer>() == null))
        {
            if (GUILayout.Button("���_�J���[�փx�C�N"))
            {
                m_Core.BakeToVertexColor();
            }
        }
    }

    private void DrawWriteToTextureUI()
    {
        EditorGUILayout.HelpBox("�����̃e�N�X�`���̓���`�����l���Ƀ}�X�N�����㏑�����܂��B", MessageType.Info);
        m_Core.TargetTextureForWrite = (Texture2D)EditorGUILayout.ObjectField("�������ݐ�e�N�X�`��", m_Core.TargetTextureForWrite, typeof(Texture2D), false);
        if (m_Core.TargetTextureForWrite != null && !m_Core.TargetTextureForWrite.isReadable)
        {
            EditorGUILayout.HelpBox("�e�N�X�`���̃C���|�[�g�ݒ�� Read/Write ��L���ɂ��Ă��������B", MessageType.Warning);
        }
        m_Core.TargetTextureChannel = (TargetChannel)EditorGUILayout.EnumFlagsField("�������ݐ�`�����l��", m_Core.TargetTextureChannel);
        DrawBlurSettings();
        using (new EditorGUI.DisabledScope(!m_Core.IsReady() || m_Core.TargetTextureForWrite == null || !m_Core.TargetTextureForWrite.isReadable))
        {
            if (GUILayout.Button("�w��`�����l���֏�������"))
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
            m_Core.EnableBlur = EditorGUILayout.ToggleLeft("�֊s�̂ڂ�����L���ɂ���", m_Core.EnableBlur);
            using (new EditorGUI.DisabledScope(!m_Core.EnableBlur))
            {
                int oldRadius = m_Core.BlurRadius; int oldIterations = m_Core.BlurIterations;
                m_Core.BlurRadius = EditorGUILayout.IntSlider("�ڂ������a (px)", m_Core.BlurRadius, 1, 50);
                m_Core.BlurIterations = EditorGUILayout.IntSlider("�ڂ������x (������)", m_Core.BlurIterations, 1, 5);
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
            // �����g���C�A���O����Ńh���b�O���Ă��������d�����Ȃ��悤�ɂ���
            if (m_HoverTriangle != m_LastProcessedTriangle)
            {
                Undo.RecordObject(this, "�|���S���I�𑀍�");
                if (m_Core.CurrentTool == ToolMode.Paint && m_Core.m_PaintableTexture != null)
                {
                    Undo.RegisterCompleteObjectUndo(m_Core.m_PaintableTexture, "�e�N�X�`���y�C���g");
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
                Undo.RecordObject(this, "�|���S���I�𑀍�(2D)");
                if (m_Core.CurrentTool == ToolMode.Paint && m_Core.m_PaintableTexture != null)
                {
                    Undo.RegisterCompleteObjectUndo(m_Core.m_PaintableTexture, "�e�N�X�`���y�C���g(2D)");
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
        // �V�[���r���[�̃J�������擾
        var sceneCam = SceneView.currentDrawingSceneView.camera;
        if (sceneCam == null) return;

        // �^�[�Q�b�g�I�u�W�F�N�g��Transform���擾
        Transform targetTransform = m_Core.TargetRenderer.transform;

        using (new Handles.DrawingScope(targetTransform.localToWorldMatrix))
        {
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            var verts = m_Core.Mesh.vertices;
            var tris = m_Core.Mesh.triangles;

            var linePoints = new List<Vector3>(m_Core.SelectedTriangles.Count * 6);

            // �J�����̎����x�N�g�����I�u�W�F�N�g�̃��[�J����Ԃɕϊ�
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
                    // �|���S���̖@�����v�Z
                    Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;

                    // �@���ƃJ�����̎����x�N�g���̓��ς��v�Z
                    // ���ς�0���傫���ꍇ�A�|���S���̓J�������猩�ė����������Ă���
                    if (Vector3.Dot(normal, camForwardLocal) > 0)
                    {
                        continue; // ���̃|���S���͕`�悵�Ȃ�
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
        Undo.RecordObject(this, "�^�[�Q�b�g�ύX");
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