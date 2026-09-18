using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{

/// <summary>
/// 输送工位诊断日志（会话内单文件共享）。
/// 路径：<c>{项目根}/Log/{会话时间戳}.log</c>。
/// </summary>
public sealed class ConveyorMotionDiagLog : IDisposable
{
    private static ConveyorMotionDiagLog _shared;
    private static int _refCount;
    private static bool _pathLogged;

    private StreamWriter _writer;
    private readonly string _filePath;

    public string FilePath => _filePath;

    /// <summary>项目根目录（Assets 的上一级）；打包后为可执行文件所在目录。</summary>
    private static string ProjectRoot =>
        Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

    private ConveyorMotionDiagLog(string filePath)
    {
        _filePath = filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath) ?? Path.Combine(ProjectRoot, "Log"));
        _writer = new StreamWriter(_filePath, append: false, Encoding.UTF8)
        {
            AutoFlush = true
        };
        WriteRaw("-", "OPEN", $"path={_filePath}");
    }

    /// <summary>取得会话共享写入器；首次创建文件并只打印一次路径。</summary>
    public static ConveyorMotionDiagLog Acquire()
    {
        if (_shared == null)
        {
            var session = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var path = Path.Combine(ProjectRoot, "Log", session + ".log");
            _shared = new ConveyorMotionDiagLog(path);
            if (!_pathLogged)
            {
                _pathLogged = true;
                Debug.Log($"[ConveyorDiag] session log -> {path}");
            }
        }

        _refCount++;
        return _shared;
    }

    public void Write(string stationKey, string evt, string detail)
    {
        if (_writer == null) return;
        WriteRaw(string.IsNullOrEmpty(stationKey) ? "-" : stationKey, evt, detail);
    }

    /// <summary>工位释放引用；最后一个关闭文件。</summary>
    public void Dispose()
    {
        if (_shared != this) return;

        _refCount = Mathf.Max(0, _refCount - 1);
        if (_refCount > 0) return;

        try
        {
            WriteRaw("-", "CLOSE", string.Empty);
            _writer?.Dispose();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ConveyorDiag] 关闭日志失败: {e.Message}");
        }

        _writer = null;
        _shared = null;
        _pathLogged = false;
    }

    private void WriteRaw(string stationKey, string evt, string detail)
    {
        if (_writer == null) return;
        try
        {
            var line = string.IsNullOrEmpty(detail)
                ? $"[{DateTime.Now:HH:mm:ss.fff}][f={Time.frameCount}] [{stationKey}] {evt}"
                : $"[{DateTime.Now:HH:mm:ss.fff}][f={Time.frameCount}] [{stationKey}] {evt} | {detail}";
            _writer.WriteLine(line);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ConveyorDiag] 写日志失败: {e.Message}");
        }
    }
}
}
