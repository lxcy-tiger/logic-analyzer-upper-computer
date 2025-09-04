using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace STM32LogicAnalyzer;

public partial class MainWindow : Window
{
    private const int MAXPoint = 12500;
    private const int MINPoint = 1;
    private SerialPort serialPort;

    public MainWindow()
    {
        InitializeComponent();
        InitializePlots();
    }

    byte[] buffer = new byte[15000];
    uint start = 0;
    uint end = 0;
    public ObservableCollection<string> ports { get; } = new(SerialPort.GetPortNames());

    private void InitializeSerialPort()
    {
        if (portAvailable.SelectionBoxItem == null)
        {
            SerialStatus.Text = "请选择串口";
            SerialStatus.Foreground = new SolidColorBrush(Colors.Red);
            return;
        }

        serialPort = new SerialPort((string)portAvailable.SelectionBoxItem, 1);
        serialPort.DataReceived += (s, e) =>
        {
            int toRead = serialPort.BytesToRead;
            byte[] temp = new byte[toRead];
            int read = serialPort.Read(temp, 0, toRead);
            for (int i = 0; i < read && end < MAXPoint; i++)
            {
                buffer[end++] = temp[i];
            }

            Dispatcher.UIThread.Post(() => receiveStatus.Text = $"已接收：{end/5}");
            if (end == Target)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    //在这里绘图
                    while (ParseData()) ;
                    Console.WriteLine($"绘制数据点：共 {steps.Count} 个事件");
                    DrawUpOverTime(steps);
                });
            }
        };

        try
        {
            serialPort.Open();
            SerialStatus.Text = "串口已打开";
            SerialStatus.Foreground = new SolidColorBrush(Colors.Green);
        }
        catch (Exception ex)
        {
            SerialStatus.Text = "串口打开失败: " + ex.Message;
            Console.WriteLine(SerialStatus.Text);
            SerialStatus.Foreground = new SolidColorBrush(Colors.Red);
        }

        Console.WriteLine(serialPort.IsOpen);
    }


    private void openSerial_OnClick(object? sender, RoutedEventArgs e)
    {
        InitializeSerialPort();
    }

    private void refreshSerial_OnClick(object? sender, RoutedEventArgs e)
    {
        ports.Clear();
        foreach (var port in SerialPort.GetPortNames())
        {
            ports.Add(port);
        }

        // 如果之前有选中的串口断开，重置状态提示
        if (serialPort?.IsOpen != true) return;
        serialPort.Close();
        SerialStatus.Text = "串口未打开";
        SerialStatus.Foreground = new SolidColorBrush(Colors.Orange);
    }

    private int Target = 0; //目标采集点数

    private void StartSampingBtn_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!serialPort.IsOpen) return;
        if (!int.TryParse(pointsWillReceive.Text, out Target)) return;
        Target *= 5;//每个点对应5byte数据
        start = 0;
        end = 0;
        Target = Math.Clamp(Target, MINPoint, MAXPoint);
        steps = [];
        byte[] send =
        [
            255, 0, (byte)((Target >> 8) & 0xFF), (byte)(Target & 0xFF)
        ];
        serialPort.Write(send, 0, 4);
    }


    private PlotModel PlotModel0 { get; set; }
    private PlotModel PlotModel1 { get; set; }
    private PlotModel PlotModel2 { get; set; }
    private StairStepSeries series0, series1, series2;
    private LinearAxis xAxis0, xAxis1, xAxis2;

    private void InitializePlots()
    {
        PlotModel0 = new PlotModel();
        PlotModel1 = new PlotModel();
        PlotModel2 = new PlotModel();

        series0 = new StairStepSeries { Title = "通道 0", Color = OxyColors.Red, StrokeThickness = 2 };
        series1 = new StairStepSeries { Title = "通道 1", Color = OxyColors.Green, StrokeThickness = 2 };
        series2 = new StairStepSeries { Title = "通道 2", Color = OxyColors.Blue, StrokeThickness = 2 };

        PlotModel0.Series.Add(series0);
        PlotModel1.Series.Add(series1);
        PlotModel2.Series.Add(series2);

        xAxis0 = new LinearAxis { Position = AxisPosition.Bottom, Title = "微秒（us）", MinimumRange = 10 };
        xAxis1 = new LinearAxis { Position = AxisPosition.Bottom, Title = "微秒（us）", MinimumRange = 10 };
        xAxis2 = new LinearAxis { Position = AxisPosition.Bottom, Title = "微秒（us）", MinimumRange = 10 };

        PlotModel0.Axes.Add(xAxis0);
        PlotModel1.Axes.Add(xAxis1);
        PlotModel2.Axes.Add(xAxis2);

        // Y 轴配置：禁止缩放和平移
        var yAxis0 = new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "通道 0",
            Minimum = -0.1,
            Maximum = 1.1,
            IsZoomEnabled = false,
            IsPanEnabled = false
        };
        var yAxis1 = new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "通道 1",
            Minimum = -0.1,
            Maximum = 1.1,
            IsZoomEnabled = false,
            IsPanEnabled = false
        };
        var yAxis2 = new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "通道 2",
            Minimum = -0.1,
            Maximum = 1.1,
            IsZoomEnabled = false,
            IsPanEnabled = false
        };

        PlotModel0.Axes.Add(yAxis0);
        PlotModel1.Axes.Add(yAxis1);
        PlotModel2.Axes.Add(yAxis2);
        // 绑定到 UI
        plotView0.Model = PlotModel0;
        plotView1.Model = PlotModel1;
        plotView2.Model = PlotModel2;

        // X轴联动
        xAxis0.AxisChanged += OnAxisChanged;
        xAxis1.AxisChanged += OnAxisChanged;
        xAxis2.AxisChanged += OnAxisChanged;
    }

    private bool isSyncing = false;

    private void OnAxisChanged(object sender, AxisChangedEventArgs e)
    {
        if (isSyncing) return;
        isSyncing = true;

        var axis = sender as LinearAxis;
        double min = axis.ActualMinimum;
        double max = axis.ActualMaximum;

        if (axis == xAxis0)
        {
            SyncAxis(xAxis1, min, max);
            SyncAxis(xAxis2, min, max);
        }
        else if (axis == xAxis1)
        {
            SyncAxis(xAxis0, min, max);
            SyncAxis(xAxis2, min, max);
        }
        else if (axis == xAxis2)
        {
            SyncAxis(xAxis0, min, max);
            SyncAxis(xAxis1, min, max);
        }

        isSyncing = false;
    }

    private void SyncAxis(LinearAxis axis, double min, double max)
    {
        axis.Zoom(min, max);
        axis.PlotModel.InvalidatePlot(false);
    }

    private class Step(uint inputIndex, bool up, double tick)
    {
        public uint InputIndex = inputIndex;
        public bool Up = up;
        public double Tick = tick;

        public override string ToString()
        {
            return "InputIndex:" + InputIndex + " Up:" + Up + " Tick:" + Tick + "\r\n";
        }

        public Step(Step old, uint newIndex) : this(newIndex, old.Up, old.Tick)
        {
        }
    };

    List<Step> steps = [];

    private bool ParseData() //单次整理数据
    {
        while (start != end && ((buffer[start] & 0xf8) != 0xf8) && start < end - 5)
        {
            start++;
        }

        if (start >= end - 5)
        {
            return false;
        }

        uint Tick = BitConverter.ToUInt32(buffer, (int)(1 + start));
        for (int i = 0; i < 3; ++i)
        {
            steps.Add(new Step((uint)i, (buffer[start] & (1u << i)) != 0, Tick/72.0));
        }

        start += 5;
        return true;
    }

    private void DrawUpOverTime(List<Step> steps)
    {
        series0.Points.Clear();
        series1.Points.Clear();
        series2.Points.Clear();

        var sortedSteps = steps.OrderBy(s => s.Tick).ToList();
        if (sortedSteps.Count == 0) return;

        // 减去首点时间，归零起点
        var firstTick = sortedSteps[0].Tick;
        var lastTick = sortedSteps[^1].Tick;
        foreach (var step in sortedSteps)
        {
            step.Tick -= firstTick;
        }

        // 计算最小时间间隔（用于设置最小缩放范围）
        double minInterval = double.MaxValue;
        var uniqueTicks = sortedSteps.Select(s => s.Tick).Distinct().OrderBy(t => t).ToArray();
        for (int i = 1; i < uniqueTicks.Length; i++)
        {
            var diff = uniqueTicks[i] - uniqueTicks[i - 1];
            if (diff > 0) minInterval = Math.Min(minInterval, diff);
        }

        // 如果只有一个点，设定默认最小间隔
        if (minInterval == double.MaxValue) minInterval = 10;

        // 总时间跨度
        double totalSpan = lastTick - firstTick;

        // 安全：避免太小或太大
        minInterval = Math.Max(minInterval, 1); // 至少允许放大到 1us
        totalSpan = Math.Max(totalSpan, 10);    // 至少显示 10us 宽度

        // 更新每个 PlotModel 的 X 轴设置
        ConfigureAxis(xAxis0, totalSpan, minInterval);
        ConfigureAxis(xAxis1, totalSpan, minInterval);
        ConfigureAxis(xAxis2, totalSpan, minInterval);

        // 添加数据点
        foreach (var step in sortedSteps)
        {
            var point = new DataPoint(step.Tick, step.Up ? 1 : 0);
            switch (step.InputIndex)
            {
                case 0: series0.Points.Add(point); break;
                case 1: series1.Points.Add(point); break;
                case 2: series2.Points.Add(point); break;
            }
        }

        // 刷新图表
        PlotModel0.InvalidatePlot(true);
        PlotModel1.InvalidatePlot(true);
        PlotModel2.InvalidatePlot(true);
    }
    private void ConfigureAxis(LinearAxis axis, double totalSpan, double minInterval)
    {
        // 设置整体数据范围（最大可视范围）
        axis.AbsoluteMinimum = -totalSpan * 0.05; // 留一点左边空白
        axis.AbsoluteMaximum = totalSpan * 1.05;  // 留一点右边空白
        axis.Minimum = axis.AbsoluteMinimum;
        axis.Maximum = axis.AbsoluteMaximum;

        // 关键：设置最小缩放范围（不能比最小时间间隔还小）
        axis.MinimumRange = minInterval;          // 最小允许缩放到一个最小时间间隔
        axis.MaximumRange = totalSpan * 1.1;      // 最大不能超过整个数据范围太多

        // 可选：启用自动调整范围
        axis.Zoom(0, totalSpan); // 初始显示全部
    }
}