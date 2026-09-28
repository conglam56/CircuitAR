using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CircuitSolver - Bộ giải thuật trung tâm Giai đoạn 3: Logic mạch kín & Mô phỏng điện học.
/// - Nhận diện toàn bộ linh kiện và liên kết dây trên bàn AR.
/// - Phát hiện Hở mạch (Open Circuit), Đoản mạch (Short Circuit) hoặc Mạch kín hợp lệ.
/// - Giải hệ phương trình thế nút để tính dòng điện I và hiệu điện thế U chính xác theo định luật Ohm.
/// - Phân phối dữ liệu xuống BulbComponent, AmmeterComponent, VoltmeterComponent.
/// </summary>
public class CircuitSolver : MonoBehaviour
{
    public static CircuitSolver Instance { get; private set; }

    [Header("--- TRẠNG THÁI MẠCH HIỆN TẠI ---")]
    public bool isCircuitClosed = false;
    public bool isShortCircuit = false;
    public float totalCurrent = 0f;
    public float equivalentResistance = 0f;

    [Header("--- THÔNG BÁO GIAO DIỆN (UI FEEDBACK) ---")]
    public ARFloatingBubbleMenu bubbleMenu;

    private bool isSolving = false;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void OnEnable()
    {
        // 1. Đăng ký sự kiện thay đổi trạng thái mô phỏng (Nút góc trên bên phải)
        if (CircuitSimulationManager.Instance != null)
        {
            CircuitSimulationManager.Instance.OnSimulationModeChanged += OnSimulationStateChanged;
        }

        // 2. Đăng ký sự kiện cấu trúc mạch thay đổi (Nối dây, tháo dây, gạt khóa K, xoay/xóa linh kiện)
        CircuitComponent.OnCircuitGraphDirty += RequestSolve;
    }

    void OnDisable()
    {
        if (CircuitSimulationManager.Instance != null)
        {
            CircuitSimulationManager.Instance.OnSimulationModeChanged -= OnSimulationStateChanged;
        }
        CircuitComponent.OnCircuitGraphDirty -= RequestSolve;
    }

    private void OnSimulationStateChanged(bool isSimulating)
    {
        if (isSimulating)
        {
            SolveCircuit();
        }
        else
        {
            ResetAllComponents();
        }
    }

    public void RequestSolve()
    {
        // Chỉ chạy giải thuật và cập nhật hiển thị khi đang bật chế độ Mô Phỏng
        if (CircuitSimulationManager.Instance != null && CircuitSimulationManager.Instance.isSimulationMode)
        {
            SolveCircuit();
        }
    }

    /// <summary>
    /// Thuật toán giải mạch điện tổng quát
    /// </summary>
    public void SolveCircuit()
    {
        if (isSolving) return;
        isSolving = true;

        try
        {
            ExecuteCircuitSimulation();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CircuitSolver] Lỗi trong quá trình tính toán mạch: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            isSolving = false;
        }
    }

    private void ExecuteCircuitSimulation()
    {
        // 1. Thu thập tất cả các linh kiện đang hoạt động trên bàn
#if UNITY_2023_1_OR_NEWER
        CircuitComponent[] allComponents = FindObjectsByType<CircuitComponent>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        CircuitComponent[] allComponents = FindObjectsOfType<CircuitComponent>();
#endif

        if (allComponents == null || allComponents.Length == 0)
        {
            SetCircuitState(false, false, 0f, 0f);
            return;
        }

        // 2. Tìm nguồn điện (Battery)
        BatteryComponent battery = null;
        List<CircuitComponent> activeComponents = new List<CircuitComponent>();

        foreach (var comp in allComponents)
        {
            if (comp == null || !comp.gameObject.activeInHierarchy) continue;
            activeComponents.Add(comp);
            if (comp is BatteryComponent b && battery == null)
            {
                battery = b;
            }
        }

        if (battery == null || battery.terminalA == null || battery.terminalB == null)
        {
            ResetComponents(activeComponents);
            SetCircuitState(false, false, 0f, 0f);
            Debug.Log("<color=yellow>[CircuitSolver]</color> Mạch chưa có nguồn điện (Pin)!");
            return;
        }

        // 3. Gom các đầu cực nối dây thành các Nút Điện Thế (Equipotential Nets)
        List<Terminal> allTerminals = new List<Terminal>();
        foreach (var comp in activeComponents)
        {
            if (comp.terminalA != null && !allTerminals.Contains(comp.terminalA)) allTerminals.Add(comp.terminalA);
            if (comp.terminalB != null && !allTerminals.Contains(comp.terminalB)) allTerminals.Add(comp.terminalB);
        }

        Dictionary<Terminal, int> terminalToNet = new Dictionary<Terminal, int>();
        int netCount = 0;

        foreach (var t in allTerminals)
        {
            if (!terminalToNet.ContainsKey(t))
            {
                // Lan truyền DFS để gom toàn bộ các cực có dây nối trực tiếp với nhau thành 1 Net
                Queue<Terminal> queue = new Queue<Terminal>();
                queue.Enqueue(t);
                terminalToNet[t] = netCount;

                while (queue.Count > 0)
                {
                    Terminal curr = queue.Dequeue();
                    foreach (var neighbor in curr.connectedTerminals)
                    {
                        if (neighbor != null && neighbor.gameObject.activeInHierarchy && !terminalToNet.ContainsKey(neighbor))
                        {
                            terminalToNet[neighbor] = netCount;
                            queue.Enqueue(neighbor);
                        }
                    }
                }
                netCount++;
            }
        }

        int netPos = terminalToNet[battery.terminalA];
        int netNeg = terminalToNet[battery.terminalB];

        // 4. KIỂM TRA ĐOẢN MẠCH TRỰC TIẾP (Dead Short Circuit)
        if (netPos == netNeg)
        {
            // Cực dương và cực âm của pin bị nối tắt trực tiếp bằng dây dẫn!
            ResetComponents(activeComponents);
            SetCircuitState(true, true, 0f, 0f);
            TriggerCircuitWarning("NGẮN MẠCH (Đoản mạch trực tiếp tại nguồn)!");
            return;
        }

        // 5. THIẾT LẬP HỆ PHƯƠNG TRÌNH THẾ NÚT MẠNG (Nodal Admittance Matrix)
        // Hệ gồm N phương trình với N thế nút V[0..N-1].
        // Chọn Nút Cực Âm làm Nút Gốc chuẩn: V[netNeg] = 0V
        // Chọn Nút Cực Dương có điện thế: V[netPos] = E (Suất điện động của pin)
        int N = netCount;
        double[,] G = new double[N, N];
        double[] I_vector = new double[N];

        // Thêm độ dẫn điện rò cực nhỏ (1e-9 Siemens) để tránh ma trận suy biến khi có nhánh treo
        for (int i = 0; i < N; i++)
        {
            G[i, i] += 1e-9;
        }

        // Nạp độ dẫn điện g = 1/R của từng linh kiện (Bóng đèn, Khóa K, Ampe kế, Vôn kế) vào ma trận
        foreach (var comp in activeComponents)
        {
            if (comp == battery) continue;
            if (comp.terminalA == null || comp.terminalB == null) continue;

            int u = terminalToNet[comp.terminalA];
            int v = terminalToNet[comp.terminalB];

            if (u == v) continue; // Hai cực của cùng 1 linh kiện bị nối tắt dây ngoài

            float r = Mathf.Max(comp.resistance, 0.0001f);
            double g = 1.0 / r;

            G[u, u] += g;
            G[v, v] += g;
            G[u, v] -= g;
            G[v, u] -= g;
        }

        // Áp đặt điều kiện biên cho Nút Nguồn:
        // Cố định V[netNeg] = 0
        for (int j = 0; j < N; j++) G[netNeg, j] = 0;
        G[netNeg, netNeg] = 1.0;
        I_vector[netNeg] = 0.0;

        // Cố định V[netPos] = battery.voltageSource
        for (int j = 0; j < N; j++) G[netPos, j] = 0;
        G[netPos, netPos] = 1.0;
        I_vector[netPos] = battery.voltageSource;

        // 6. GIẢI HỆ PHƯƠNG TRÌNH BẰNG PHƯƠNG PHÁP KHỬ GAUSS (Gaussian Elimination)
        double[] V = SolveLinearSystem(G, I_vector, N);

        if (V == null)
        {
            // Không giải được thế nút -> Mạch hở
            ResetComponents(activeComponents);
            SetCircuitState(false, false, 0f, 0f);
            return;
        }

        // Cập nhật điện thế tương đối V tại từng đầu cực vật lý
        foreach (var t in allTerminals)
        {
            int netIdx = terminalToNet[t];
            t.electricPotential = (float)V[netIdx];
        }

        // 7. TÍNH TOÁN DÒNG ĐIỆN VÀ SỤT ÁP CHO TỪNG LINH KIỆN
        float totalLoadCurrent = 0f;
        float totalLoadResistance = 0f;
        bool hasLoadComponent = false;

        foreach (var comp in activeComponents)
        {
            if (comp == battery) continue;
            if (comp.terminalA == null || comp.terminalB == null)
            {
                comp.ResetElectricalState();
                continue;
            }

            int u = terminalToNet[comp.terminalA];
            int v = terminalToNet[comp.terminalB];

            float uDrop = Mathf.Abs((float)(V[u] - V[v]));
            float current = uDrop / Mathf.Max(comp.resistance, 0.0001f);

            // Kiểm tra tải tiêu thụ thực tế (Bóng đèn)
            if (comp is BulbComponent)
            {
                hasLoadComponent = true;
            }

            // Nếu nối vào nhánh có dòng điện chạy qua cực dương pin
            if (u == netPos || v == netPos)
            {
                totalLoadCurrent += current;
            }

            // Phân bổ dữ liệu xuống từng linh kiện để cập nhật hiển thị (Đèn sáng, kim quay)
            comp.ApplyElectricalState(current, uDrop);
        }

        // 8. ĐÁNH GIÁ MẠCH KÍN, HỞ MẠCH HOẶC ĐOẢN MẠCH
        // Dòng điện tổng qua pin
        float calculatedCurrent = totalLoadCurrent;
        if (calculatedCurrent > 0.001f)
        {
            totalLoadResistance = battery.voltageSource / calculatedCurrent;
        }

        // Nếu điện trở tải ngoài quá nhỏ (< 0.1 Ohm) và không có bóng đèn cản dòng -> Ngắn mạch / Đoản mạch
        if (calculatedCurrent > 15f || (calculatedCurrent > 1f && totalLoadResistance < 0.1f && !hasLoadComponent))
        {
            ResetComponents(activeComponents);
            SetCircuitState(true, true, 0f, totalLoadResistance);
            TriggerCircuitWarning("CẢNH BÁO: MẠCH BỊ NGẮN MẠCH (Đoản mạch tải quá nhỏ)!");
            return;
        }

        // Nếu dòng điện quá nhỏ (< 1mA) -> Mạch hở (Khóa K mở hoặc dây chưa kín vòng)
        if (calculatedCurrent < 0.001f)
        {
            ResetComponents(activeComponents);
            SetCircuitState(false, false, 0f, 0f);
            Debug.Log("<color=orange>[CircuitSolver]</color> Mạch HỞ (Không có dòng điện chạy qua)");
            return;
        }

        // Mạch kín hoàn hảo!
        battery.ApplyElectricalState(calculatedCurrent, battery.voltageSource);
        SetCircuitState(true, false, calculatedCurrent, totalLoadResistance);

        Debug.Log($"<color=green>[CircuitSolver] MẠCH KÍN THÀNH CÔNG!</color> I = {calculatedCurrent:F3} A, R_td = {totalLoadResistance:F2} Ohm");
    }

    /// <summary>
    /// Thuật toán khử Gauss giải hệ ma trận Ax = B
    /// </summary>
    private double[] SolveLinearSystem(double[,] A, double[] b, int n)
    {
        double[,] M = (double[,])A.Clone();
        double[] x = (double[])b.Clone();

        for (int i = 0; i < n; i++)
        {
            // Tìm phần tử khử lớn nhất (Partial Pivoting)
            int maxRow = i;
            double maxVal = Math.Abs(M[i, i]);
            for (int k = i + 1; k < n; k++)
            {
                if (Math.Abs(M[k, i]) > maxVal)
                {
                    maxVal = Math.Abs(M[k, i]);
                    maxRow = k;
                }
            }

            if (maxVal < 1e-12) return null; // Ma trận suy biến

            // Hoán vị hàng
            if (maxRow != i)
            {
                for (int k = i; k < n; k++)
                {
                    double tmp = M[i, k];
                    M[i, k] = M[maxRow, k];
                    M[maxRow, k] = tmp;
                }
                double tmpB = x[i];
                x[i] = x[maxRow];
                x[maxRow] = tmpB;
            }

            // Khử các hàng bên dưới
            for (int k = i + 1; k < n; k++)
            {
                double factor = M[k, i] / M[i, i];
                for (int j = i; j < n; j++)
                {
                    M[k, j] -= factor * M[i, j];
                }
                x[k] -= factor * x[i];
            }
        }

        // Thế ngược (Back Substitution)
        double[] result = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double sum = x[i];
            for (int j = i + 1; j < n; j++)
            {
                sum -= M[i, j] * result[j];
            }
            result[i] = sum / M[i, i];
        }

        return result;
    }

    private void SetCircuitState(bool closed, bool shorted, float current, float resistance)
    {
        isCircuitClosed = closed;
        isShortCircuit = shorted;
        totalCurrent = current;
        equivalentResistance = resistance;
    }

    private void ResetComponents(List<CircuitComponent> components)
    {
        foreach (var c in components)
        {
            if (c != null) c.ResetElectricalState();
        }
    }

    public void ResetAllComponents()
    {
#if UNITY_2023_1_OR_NEWER
        CircuitComponent[] comps = FindObjectsByType<CircuitComponent>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        CircuitComponent[] comps = FindObjectsOfType<CircuitComponent>();
#endif
        foreach (var c in comps)
        {
            if (c != null) c.ResetElectricalState();
        }
        SetCircuitState(false, false, 0f, 0f);
    }

    private void TriggerCircuitWarning(string message)
    {
        Debug.LogError($"<color=red>[CircuitSolver]</color> {message}");
        if (bubbleMenu == null)
        {
#if UNITY_2023_1_OR_NEWER
            bubbleMenu = FindFirstObjectByType<ARFloatingBubbleMenu>();
#else
            bubbleMenu = FindObjectOfType<ARFloatingBubbleMenu>();
#endif
        }

        if (bubbleMenu != null)
        {
            bubbleMenu.ShowToastNotification(message, 3.5f);
        }
    }
}