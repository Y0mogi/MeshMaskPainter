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
    /// ���j���[����E�B���h�E���J���܂��B
    /// </summary>
    [MenuItem("Tools/MMP_Var1.0")]
    public static void ShowWindow()
    {
        var w = GetWindow<MeshMaskPainterWindow>("MMP");
        w.minSize = new Vector2(800, 900);
    }

    /// <summary>
    /// �E�B���h�E���L���ɂȂ����ۂɌĂяo����܂��B
    /// </summary>
    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        Undo.undoRedoPerformed += OnUndoRedo;
        m_Core.UndoRedoPerformed += OnUndoRedo;
    }

    /// <summary>
    /// �E�B���h�E�������ɂȂ����ۂɌĂяo����܂��B
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
    /// Undo/Redo���삪���s���ꂽ�ۂ�UI���X�V���܂��B
    /// </summary>
    private void OnUndoRedo()
    {
        m_Core.UpdatePreviewTexture(m_SharpPreviewTex);
        Repaint();
    }

    /// <summary>
    /// �G�f�B�^�E�B���h�E��UI��`�悵�܂��B
    /// </summary>
    private void OnGUI()
    {
        EditorGUILayout.BeginHorizontal();

        DrawPreviewPanel();
        DrawControlPanel();

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.HelpBox("Scene�r���[�Ń��b�V����I���B�y�C���g=�I��ǉ�, �����S��=�I�������B\n�q���g: Shift�L�[�������Ȃ���N���b�N����ƁA�q�����Ă���UV�A�C�����h�S�̂�I���ł��܂��B", MessageType.Info);
    }

    /// <summary>
    /// �V�[���r���[�ł̕`��ƃC���^���N�V�������������܂��B
    /// </summary>
    /// <param name="view">�Ώۂ̃V�[���r���[�B</param>
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
    /// �����̃v���r���[�p�l����`�悵�܂��B
    /// </summary>
    private void DrawPreviewPanel()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
        {
            EditorGUILayout.LabelField("�v���r���[ (UV�}�X�N)", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope("box", GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                int newPreviewSize = EditorGUILayout.IntPopup("�v���r���[�𑜓x", m_Core.PreviewSize, new[] { "512", "1024", "2048" }, new[] { 512, 1024, 2048 });
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
                    if (!m_IsPreviewBlurred) { if (GUILayout.Button("�v���r���[�ɂڂ�����K�p")) { ApplyBlurToPreview(); m_IsPreviewBlurred = true; } }
                    else { if (GUILayout.Button("�ڂ��������v���r���[�ɖ߂�")) { m_IsPreviewBlurred = false; } }
                }
            }
        }
    }

    /// <summary>
    /// �E���̃R���g���[���p�l����`�悵�܂��B
    /// </summary>
    private void DrawControlPanel()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(350)))
        {
            EditorGUILayout.Space();

            m_TargetFoldout = EditorGUILayout.Foldout(m_TargetFoldout, "�^�[�Q�b�g�ݒ�", true, EditorStyles.foldoutHeader);
            if (m_TargetFoldout) DrawTargetSettings();

            EditorGUILayout.Space();

            m_SelectionToolsFoldout = EditorGUILayout.Foldout(m_SelectionToolsFoldout, "�I���c�[��", true, EditorStyles.foldoutHeader);
            if (m_SelectionToolsFoldout) DrawSelectionTools();

            EditorGUILayout.Space();

            m_PaintDisplayFoldout = EditorGUILayout.Foldout(m_PaintDisplayFoldout, "�y�C���g & �\���ݒ�", true, EditorStyles.foldoutHeader);
            if (m_PaintDisplayFoldout) DrawPaintAndDisplaySettings();

            EditorGUILayout.Space();

            m_ExportFoldout = EditorGUILayout.Foldout(m_ExportFoldout, "�G�N�X�|�[�g�ݒ�", true, EditorStyles.foldoutHeader);
            if (m_ExportFoldout) DrawExportSettings();
        }
    }

    /// <summary>
    /// �u�^�[�Q�b�g�ݒ�v��UI��`�悵�܂��B
    /// </summary>
    private void DrawTargetSettings()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            var newSmr = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("�X�L�����b�V�������_���[", m_Core.Smr, typeof(SkinnedMeshRenderer), true);
            if (newSmr != m_Core.Smr)
            {
                Undo.RecordObject(this, "�^�[�Q�b�g�ύX");
                m_Core.SetTarget(newSmr);
                CreatePreviewTexture();
                m_Core.UpdatePreviewTexture(m_SharpPreviewTex);
            }

            using (new EditorGUI.DisabledScope(m_Core.Smr == null))
            {
                if (GUILayout.Button("�e��}�b�v���č\�z"))
                {
                    Undo.RecordObject(this, "�}�b�v���č\�z");
                    m_Core.SetupMeshAndMappings();
                    m_Core.UpdatePreviewTexture(m_SharpPreviewTex);
                }
            }

            EditorGUILayout.Space(4);
            using (new EditorGUI.DisabledScope(m_Core.Smr == null || m_Core.Smr.sharedMaterial == null))
            {
                if (m_Core.BaseTexture == null && m_Core.Smr != null && m_Core.Smr.sharedMaterial != null) m_Core.BaseTexture = m_Core.Smr.sharedMaterial.mainTexture as Texture2D;
                m_Core.BaseTexture = (Texture2D)EditorGUILayout.ObjectField("�x�[�X�e�N�X�`�� (�Q�Ɨp)", m_Core.BaseTexture, typeof(Texture2D), false);
                if (m_Core.ShowBaseTextureInPreview && m_Core.BaseTexture != null && !m_Core.BaseTexture.isReadable)
                {
                    EditorGUILayout.HelpBox("�v���r���[�\���̂��߂ɁA�x�[�X�e�N�X�`���� Read/Write ��L���ɂ��Ă��������B", MessageType.Warning);
                }
            }
        }
    }

    /// <summary>
    /// �u�y�C���g & �\���ݒ�v��UI��`�悵�܂��B
    /// </summary>
    private void DrawPaintAndDisplaySettings()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("�y�C���g���[�h", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Toggle(m_Core.PaintMode, "�y�C���g", "Button")) m_Core.PaintMode = true;
            if (GUILayout.Toggle(!m_Core.PaintMode, "�����S��", "Button")) m_Core.PaintMode = false;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("�y�C���g�͈�", EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                m_Core.OnlyActiveSubmesh = EditorGUILayout.ToggleLeft("�A�N�e�B�u�ȃT�u���b�V���̂�", m_Core.OnlyActiveSubmesh, GUILayout.Width(180));
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
            EditorGUILayout.LabelField("�\���ݒ�", EditorStyles.miniBoldLabel);
            //m_Core.EdgeHighlightWidth = EditorGUILayout.Slider("�G�b�W�̑��� (px)", m_Core.EdgeHighlightWidth, 0.5f, 6f);
            m_Core.EdgeSelectionColor = EditorGUILayout.ColorField("�I���ς݉ӏ��̐F", m_Core.EdgeSelectionColor);
            m_Core.EdgeHighlightColor = EditorGUILayout.ColorField("�I�𒆂̃n�C���C�g�F", m_Core.EdgeHighlightColor);
            m_Core.ShowWireframe = EditorGUILayout.ToggleLeft("�I���ς݉ӏ������C���[�t���[����\��", m_Core.ShowWireframe);
            m_Core.HoverHighlight = EditorGUILayout.ToggleLeft("�z�o�[���Ƀn�C���C�g", m_Core.HoverHighlight);
            bool newShowBaseTexture = EditorGUILayout.ToggleLeft("�v���r���[�Ƀx�[�X�e�N�X�`����\��", m_Core.ShowBaseTextureInPreview);
            if (newShowBaseTexture != m_Core.ShowBaseTextureInPreview) { m_Core.ShowBaseTextureInPreview = newShowBaseTexture; m_Core.UpdatePreviewTexture(m_SharpPreviewTex); }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("���̑�", EditorStyles.miniBoldLabel);
            m_Core.AddTemporaryCollider = EditorGUILayout.ToggleLeft("�ꎞ�I��MeshCollider�������ǉ�", m_Core.AddTemporaryCollider);
        }
    }

    /// <summary>
    /// �u�I���c�[���v��UI��`�悵�܂��B
    /// </summary>
    private void DrawSelectionTools()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("�N���A")) { Undo.RecordObject(this, "�I�����N���A"); m_Core.ClearSelection(); }
                using (new EditorGUI.DisabledScope(m_Core.SelectedTriangles.Count == 0)) { if (GUILayout.Button("���]")) { Undo.RecordObject(this, "�I���𔽓]"); m_Core.InvertSelection(); } }
            }
            using (new EditorGUI.DisabledScope(m_Core.SelectedTriangles.Count == 0 || !m_Core.IsAdjacencyMapReady()))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("�I�����g�� (Grow)")) { Undo.RecordObject(this, "�I�����g��"); m_Core.GrowSelection(); }
                    if (GUILayout.Button("�I�����k�� (Shrink)")) { Undo.RecordObject(this, "�I�����k��"); m_Core.ShrinkSelection(); }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("�I����ۑ�")) { m_Core.SaveSelection(); }
                if (GUILayout.Button("�I����ǂݍ���")) { Undo.RecordObject(this, "�I����ǂݍ���"); m_Core.LoadSelection(); }
            }
        }
    }

    /// <summary>
    /// �u�G�N�X�|�[�g�ݒ�v��UI��`�悵�܂��B
    /// </summary>
    private void DrawExportSettings()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            m_Core.ExportUseBaseTextureSize = EditorGUILayout.ToggleLeft("�x�[�X�e�N�X�`���Ɠ��𑜓x�ŏo��", m_Core.ExportUseBaseTextureSize);
            using (new EditorGUI.DisabledScope(m_Core.ExportUseBaseTextureSize))
            {
                m_Core.ExportWidth = EditorGUILayout.IntPopup("�o�͉𑜓x (��)", m_Core.ExportWidth, new[] { "1024", "2048", "4096" }, new[] { 1024, 2048, 4096 });
                m_Core.ExportHeight = EditorGUILayout.IntPopup("�o�͉𑜓x (����)", m_Core.ExportHeight, new[] { "1024", "2048", "4096" }, new[] { 1024, 2048, 4096 });
            }
            m_Core.ExportAlphaZeroBackground = EditorGUILayout.ToggleLeft("�w�i�𓧉� (�A���t�@=0)", m_Core.ExportAlphaZeroBackground);
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
    }

    /// <summary>
    /// �}�E�X�J�[�\�����̃|���S������肵�܂��B
    /// </summary>
    /// <param name="e">���݂̃C�x���g�B</param>
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
    /// �V�[���r���[��ł̃N���b�N��h���b�O���͂��������܂��B
    /// </summary>
    /// <param name="e">���݂̃C�x���g�B</param>
    private void HandlePaintInput(Event e)
    {
        if (m_HoverTriangle < 0)
        {
            if (e.type == EventType.MouseUp && e.button == 0) m_LastProcessedTriangle = -1;
            return;
        }

        if (e.type == EventType.MouseDown && e.shift && e.button == 0 && !e.alt)
        {
            Undo.RecordObject(this, "UV�A�C�����h��I��");
            m_Core.SelectUVIsland(m_HoverTriangle);
            e.Use();
        }
        else if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && !e.alt)
        {
            if (m_HoverTriangle != m_LastProcessedTriangle)
            {
                if (m_Core.PaintMode)
                {
                    Undo.RecordObject(this, "�I����ǉ�");
                    m_Core.AddTriangle(m_HoverTriangle);
                }
                else
                {
                    Undo.RecordObject(this, "�I��������");
                    m_Core.RemoveTriangle(m_HoverTriangle);
                }
                m_LastProcessedTriangle = m_HoverTriangle;
            }
            e.Use();
        }

        if (e.type == EventType.MouseUp && e.button == 0) m_LastProcessedTriangle = -1;
    }

    /// <summary>
    /// �v���r���[�p�̃e�N�X�`�����������E�Đ������܂��B
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
    /// �v���r���[�ɂڂ�����K�p���܂��B
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
    /// �I��͈͂̃��C���[�t���[���ƃz�o�[�n�C���C�g���V�[���r���[�ɕ`�悵�܂��B
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
    /// �������w��ł���A���`�G�C���A�X�t���̐���`�悵�܂��B
    /// </summary>
    private void DrawLineAA(Vector3 a, Vector3 b, float width)
    {
        width = Mathf.Max(1f, width);
        Handles.DrawAAPolyLine(width, a, b);
    }
}
