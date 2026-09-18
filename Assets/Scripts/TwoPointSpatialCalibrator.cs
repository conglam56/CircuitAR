using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[RequireComponent(typeof(LineRenderer))]
public class TwoPointSpatialCalibrator : MonoBehaviour
{
    [Header("Tham chieu AR")]
    public ARRaycastManager raycastManager;
    public ARPlaneManager planeManager;
    public ARAnchorManager anchorManager;
    public SinglePlaneLockController lockController;

    [Header("Prefab mat ban (Cube mong)")]
    public GameObject tableBoardPrefab;

    [Header("UI va Tam ngam")]
    public GameObject spatialCalibrationUI;
    public GameObject reticleUI;
    public TextMeshProUGUI btnLabelTMP;
    public Text btnLabelLegacy;

    [Header("Tuong tac MediaPipe")]
    public ButtonInteractor handInteractor;

    private int step = 0;
    private Vector3 pointA;
    private Vector3 pointB;
    private ARPlane targetPlane;
    private LineRenderer guideLine;
    private static List<ARRaycastHit> hits = new List<ARRaycastHit>();

    void Awake()
    {
        guideLine = GetComponent<LineRenderer>();
        guideLine.useWorldSpace = true;
        guideLine.startWidth = 0.015f;
        guideLine.endWidth = 0.015f;
        guideLine.positionCount = 0;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        Material mat = new Material(shader);
        mat.color = Color.cyan;
        guideLine.material = mat;
    }

    void Start()
    {
        SetText("1. HUONG TAM VAO MEP TRAI\nChum ngon tay (Pinch)");
        if (handInteractor == null) handInteractor = FindFirstObjectByType<ButtonInteractor>();
        if (anchorManager == null) anchorManager = FindFirstObjectByType<ARAnchorManager>();
    }

    void Update()
    {
        // 1. Tự động vẽ đường thẳng nối Điểm A tới tâm ngắm của camera
        if (step == 1 && guideLine != null)
        {
            Vector3 currentHitPoint;
            if (TryGetScreenCenterPlane(out currentHitPoint, out _))
            {
                guideLine.positionCount = 2;
                guideLine.SetPosition(0, pointA + Vector3.up * 0.01f);
                guideLine.SetPosition(1, currentHitPoint + Vector3.up * 0.01f);
            }
        }

        // 2. Bắt cử chỉ chụm tay ngón cái + trỏ
        if (handInteractor != null && handInteractor.JustPinched)
        {
            OnTriggerPinch();
        }
    }

    private bool TryGetScreenCenterPlane(out Vector3 hitPos, out ARPlane hitPlane)
    {
        Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        // SỬA LỖI ẤN TRƯỢT: Quét cả Polygon lẫn Bounds ước lượng để bắt cực nhạy
        TrackableType trackTypes = TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds;

        if (raycastManager != null && raycastManager.Raycast(center, hits, trackTypes))
        {
            hitPos = hits[0].pose.position;
            hitPlane = planeManager != null ? planeManager.GetPlane(hits[0].trackableId) : null;
            return true;
        }
        hitPos = Vector3.zero;
        hitPlane = null;
        return false;
    }

    public void OnTriggerPinch()
    {
        Vector3 hitPoint;
        ARPlane plane;

        if (TryGetScreenCenterPlane(out hitPoint, out plane))
        {
            if (step == 0)
            {
                pointA = hitPoint;
                targetPlane = plane;
                step = 1;
                SetText("2. HUONG TAM VAO MEP PHAI\nChum ngon tay de khoa");
                Debug.Log("[Calibration] Diem A: " + pointA);
            }
            else if (step == 1)
            {
                pointB = hitPoint;
                Debug.Log("[Calibration] Diem B: " + pointB);

                if (guideLine != null) guideLine.positionCount = 0;
                FinishCalibration();
            }
        }
        else
        {
            SetText("CHUA TRUNG MAT BAN!\nLia cham quanh mat ban truoc");
        }
    }

    private void FinishCalibration()
    {
        Vector3 edgeAB = pointB - pointA;
        // Bỏ qua độ lệch cao độ Y để mép trước hoàn toàn nằm ngang phẳng
        edgeAB.y = 0;
        float length = edgeAB.magnitude;
        if (length < 0.1f) return;

        Vector3 edgeDir = edgeAB.normalized; // Trục ngang của bàn
        // Trục sâu vào trong bàn: lấy tích có hướng với phương thẳng đứng chuẩn
        Vector3 depthDir = Vector3.Cross(Vector3.up, edgeDir).normalized;

        // Đảm bảo chiều sâu luôn đâm ra xa hướng đứng của camera
        Vector3 camForwardFlat = Camera.main.transform.forward;
        camForwardFlat.y = 0;
        if (Vector3.Dot(depthDir, camForwardFlat) < 0)
        {
            depthDir = -depthDir;
        }

        float depth = 0.35f;      // Chiều sâu thảm: 35cm
        float thickness = 0.005f; // Độ dày bảng: 5mm

        // Tâm bàn đặt chuẩn xác: từ trung điểm AB lùi vào 1/2 chiều sâu
        Vector3 center = pointA + (edgeAB * 0.5f) + (depthDir * (depth * 0.5f));
        center.y = (pointA.y + pointB.y) * 0.5f - (thickness * 0.5f);

        // KHÓA HƯỚNG XOAY: Trục Z hướng theo depthDir, Trục Y hướng thẳng lên trời
        Quaternion rotation = Quaternion.LookRotation(depthDir, Vector3.up);

        Pose pose = new Pose(center, rotation);
        Transform anchorParent = null;

        if (anchorManager != null && targetPlane != null)
        {
            ARAnchor anchor = anchorManager.AttachAnchor(targetPlane, pose);
            if (anchor != null) anchorParent = anchor.transform;
        }

        if (anchorParent == null)
        {
            GameObject anchorObj = new GameObject("Board_Anchor");
            anchorObj.transform.position = center;
            anchorObj.transform.rotation = rotation;
            anchorObj.transform.localScale = Vector3.one; // Khóa tỉ lệ Anchor chuẩn 1:1:1
            anchorParent = anchorObj.transform;
        }

        if (tableBoardPrefab != null)
        {
            GameObject board = Instantiate(tableBoardPrefab, center, rotation);
            board.transform.SetParent(anchorParent, true);
            board.transform.localScale = new Vector3(length, thickness, depth);
            board.name = "ActiveCircuitBoard";
        }

        if (lockController != null)
        {
            lockController.SetManualLocked(targetPlane != null ? targetPlane.trackableId : TrackableId.invalidId);
        }

        if (spatialCalibrationUI != null) spatialCalibrationUI.SetActive(false);
        if (reticleUI != null) reticleUI.SetActive(false);

        enabled = false;
    }

    private void SetText(string content)
    {
        if (btnLabelTMP != null) btnLabelTMP.text = content;
        if (btnLabelLegacy != null) btnLabelLegacy.text = content;
    }
}