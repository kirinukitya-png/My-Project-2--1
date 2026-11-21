using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class OllamaClient : MonoBehaviour
{
    private const string TestQuestion = "こんにちは。自己紹介してください。";
    private const string AnalysisInstruction =
        "あなたはオートバトルゲームの攻略AIです。必ず日本語で、最終回答だけを書いてください。JSONを読み上げず、同じ数値や情報を繰り返さないでください。\n" +
        "渡されたJSONだけを根拠に、次の形式で簡潔に回答してください。全体は300〜500トークン程度を目安にし、各項目は要点だけにしてください。情報がなければ項目を省略してください。\n" +
        "【現在の状況】ステージ、HP、所持金、盤面人数。\n" +
        "【強み】重要な点を最大2つ。\n" +
        "【弱み】重要な点を最大2つ。\n" +
        "【シナジー】現在盤面で確認できるものと、今後狙えるものを分ける。boardCount等の数値を優先し、初期30体で完成可能なUnitを現在成立中として扱わない。盤面にいないキャラクターのUnitを盤面シナジーに数えない。\n" +
        "【次の行動】優先度順に最大3つ。boardとhandを区別する。\n" +
        "不明な情報は推測せず「不明」とし、ゲームに存在しない効果・能力・ルールを作らない。";
    [Header("Ollama 接続設定")]
    [SerializeField] private string endpoint = "http://localhost:11434/api/chat";
    [SerializeField] private string model = "qwen3:4b";

    [Header("タイムアウト（秒）")]
    [SerializeField, Min(1)] private int connectionTestTimeoutSeconds = 120;
    [SerializeField, Min(1)] private int analysisTimeoutSeconds = 600;
    [SerializeField, Min(1)] private int analysisMaxOutputTokens = 700;

    private bool requestInProgress;

    [ContextMenu("Ollama/Run Connection Test")]
    public void TestConnection()
    {
        if (!CanStartRequest("Ollama接続テスト"))
            return;
        StartCoroutine(SendTestRequest());
    }

    [ContextMenu("Ollama/Analyze Current Game State")]
    public void AnalyzeCurrentGameState()
    {
        if (!CanStartRequest("ゲーム状況の分析"))
            return;

        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
        GameStateSnapshotBuilder.Snapshot snapshot = GameStateSnapshotBuilder.Build();
        string beforeJson = JsonUtility.ToJson(snapshot, false);
        GameStateSnapshotBuilder.CompactAnalysisSnapshot compactSnapshot =
            GameStateSnapshotBuilder.CreateCompactSnapshot(snapshot);
        string snapshotJson = JsonUtility.ToJson(compactSnapshot, false);
        stopwatch.Stop();

        int beforeLength = beforeJson.Length;
        int afterLength = snapshotJson.Length;
        int reducedLength = Math.Max(0, beforeLength - afterLength);
        double reductionPercent = beforeLength > 0
            ? reducedLength * 100.0 / beforeLength
            : 0.0;

        Debug.Log(
            "分析JSON軽量化: 変更前 " + beforeLength + "文字 / 変更後 " + afterLength +
            "文字 / 削減 " + reducedLength + "文字 (" + reductionPercent.ToString("F1") +
            "%) / 生成時間 " + stopwatch.Elapsed.TotalMilliseconds.ToString("F1") + "ms",
            this);
        Debug.Log("実際に送信するゲーム状況JSON:\n" + snapshotJson, this);
        StartCoroutine(SendAnalysisRequest(snapshotJson));
    }

    private bool CanStartRequest(string operationName)
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning(operationName + "はPlay Mode中に実行してください。", this);
            return false;
        }
        if (requestInProgress)
        {
            Debug.LogWarning("Ollamaへのリクエストはすでに実行中です。", this);
            return false;
        }
        return true;
    }

    private IEnumerator SendTestRequest()
    {
        requestInProgress = true;
        ChatRequest payload = new ChatRequest
        {
            model = model,
            messages = new[] { new ChatMessage { role = "user", content = TestQuestion } },
            stream = false,
            think = false
        };
        yield return SendRequest(payload, "接続テスト", "Ollamaからの返答:\n", connectionTestTimeoutSeconds);
    }

    private IEnumerator SendAnalysisRequest(string snapshotJson)
    {
        requestInProgress = true;
        ChatRequest payload = new ChatRequest
        {
            model = model,
            messages = new[]
            {
                new ChatMessage { role = "system", content = AnalysisInstruction },
                new ChatMessage
                {
                    role = "user",
                    content = "以下はUnityから取得した現在のゲーム状況JSONです。内容だけを根拠に分析してください。\n\n" +
                              snapshotJson
                }
            },
            stream = false,
            think = false,
            options = new ChatOptions { num_predict = Mathf.Max(1, analysisMaxOutputTokens) }
        };

        Debug.Log(
            "分析リクエスト設定: think=false / num_predict=" + payload.options.num_predict +
            " / timeout=" + analysisTimeoutSeconds + "秒",
            this);
        yield return SendRequest(payload, "ゲーム状況分析", "ゲーム状況の分析結果:\n", analysisTimeoutSeconds);
    }

    private IEnumerator SendRequest(
        ChatRequest payload,
        string operationName,
        string successPrefix,
        int timeoutSeconds)
    {
        string requestJson = JsonUtility.ToJson(payload, false);
        byte[] body = Encoding.UTF8.GetBytes(requestJson);
        DateTime startedAt = DateTime.Now;
        float startedRealtime = Time.realtimeSinceStartup;

        Debug.Log(
            operationName + "送信開始: " + startedAt.ToString("yyyy-MM-dd HH:mm:ss.fff") +
            " / JSON " + requestJson.Length + "文字 (" + body.Length + " bytes) / timeout " +
            Mathf.Max(1, timeoutSeconds) + "秒",
            this);

        using (UnityWebRequest request = new UnityWebRequest(endpoint, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = Mathf.Max(1, timeoutSeconds);
            request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            yield return request.SendWebRequest();

            DateTime endedAt = DateTime.Now;
            float elapsedSeconds = Mathf.Max(0f, Time.realtimeSinceStartup - startedRealtime);
            string timing =
                "開始 " + startedAt.ToString("yyyy-MM-dd HH:mm:ss.fff") +
                " / 終了 " + endedAt.ToString("yyyy-MM-dd HH:mm:ss.fff") +
                " / 経過 " + elapsedSeconds.ToString("F1") + "秒";

            if (request.result != UnityWebRequest.Result.Success)
            {
                bool timedOut = request.error != null &&
                    request.error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0;
                string failureType = timedOut
                    ? "タイムアウト"
                    : request.responseCode > 0 ? "HTTPエラー" : "通信エラー";

                Debug.LogError(
                    operationName + failureType + ": HTTP " + request.responseCode +
                    " / " + request.error + " / " + timing +
                    (string.IsNullOrEmpty(request.downloadHandler.text)
                        ? string.Empty
                        : "\n応答: " + request.downloadHandler.text),
                    this);
                requestInProgress = false;
                yield break;
            }

            Debug.Log(
                operationName + "送信終了: HTTP " + request.responseCode + " / " + timing,
                this);

            ChatResponse response = null;
            try
            {
                response = JsonUtility.FromJson<ChatResponse>(request.downloadHandler.text);
            }
            catch (Exception exception)
            {
                Debug.LogError("Ollama応答JSONの解析失敗: " + exception.Message + " / " + timing, this);
                requestInProgress = false;
                yield break;
            }

            LogResponseMetrics(operationName, response, timing);

            if (response == null || response.message == null ||
                string.IsNullOrWhiteSpace(response.message.content))
            {
                string thinkingOnly = response != null && response.message != null &&
                    !string.IsNullOrEmpty(response.message.thinking)
                    ? "message.contentは空で、message.thinkingのみ存在します（" +
                      response.message.thinking.Length + "文字）。thinkingは回答として扱いません。"
                    : "message.contentがありません。";
                Debug.LogError(
                    "Ollama応答を分析結果として扱えません: " + thinkingOnly +
                    " HTTP " + request.responseCode + " / " + timing +
                    "\n" + request.downloadHandler.text,
                    this);
                requestInProgress = false;
                yield break;
            }

            if (response.done_reason == "length")
            {
                Debug.LogError(
                    operationName + "の出力がトークン上限に達して途中終了しました。" +
                    " num_predict=" + (payload.options != null ? payload.options.num_predict : 0) +
                    " / eval_count=" + response.eval_count + " / " + timing,
                    this);
                Debug.Log(successPrefix + response.message.content, this);
                requestInProgress = false;
                yield break;
            }

            Debug.Log(successPrefix + response.message.content, this);
        }
        requestInProgress = false;
    }

    private void LogResponseMetrics(string operationName, ChatResponse response, string timing)
    {
        if (response == null)
        {
            Debug.LogError(operationName + "の応答メタデータを取得できません。 / " + timing, this);
            return;
        }

        string content = response.message != null ? response.message.content : null;
        string thinking = response.message != null ? response.message.thinking : null;
        Debug.Log(
            operationName + "応答情報: done=" + response.done +
            " / done_reason=" + (string.IsNullOrEmpty(response.done_reason) ? "(なし)" : response.done_reason) +
            " / prompt_eval_count=" + response.prompt_eval_count +
            " / eval_count=" + response.eval_count +
            " / total_duration=" + response.total_duration + "ns" +
            " / eval_duration=" + response.eval_duration + "ns" +
            " / message.content=" + (content == null ? "null" : content.Length + "文字") +
            " / message.thinking=" + (!string.IsNullOrEmpty(thinking)) +
            "（" + (thinking == null ? 0 : thinking.Length) + "文字） / " + timing,
            this);
    }

    [Serializable]
    private class ChatRequest
    {
        public string model;
        public ChatMessage[] messages;
        public bool stream;
        public bool think;
        public ChatOptions options;
    }

    [Serializable]
    private class ChatOptions { public int num_predict; }

    [Serializable]
    private class ChatMessage
    {
        public string role = string.Empty;
        public string content = string.Empty;
    }

    [Serializable]
    private class ChatResponse
    {
        public ChatResponseMessage message = new ChatResponseMessage();
        public bool done;
        public string done_reason = string.Empty;
        public int prompt_eval_count;
        public int eval_count;
        public long total_duration;
        public long eval_duration;
    }

    [Serializable]
    private class ChatResponseMessage
    {
        public string role = string.Empty;
        public string content = string.Empty;
        public string thinking = string.Empty;
    }
}