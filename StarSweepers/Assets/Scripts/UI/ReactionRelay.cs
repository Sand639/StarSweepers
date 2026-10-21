using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **選んだリアクションを、通信で全員の画面に知らせる係。**（試作・2026/10/21）
///
/// `ReactionSystem.prefab` の一番上に付いている。同じプレハブの <see cref="ReactionWheel"/> で選ぶと、
///
/// 1. 自分 → ホストへ「○番を選んだ」と送る
/// 2. ホストがその人の名前を調べ、全員へ「（名前）が○番」と送る
/// 3. 全員の画面で <see cref="ReactionFeed"/> が、画面の右端に「名前＋スタンプ」を出す
///
/// ## 使わないとき
///
/// **ロビーのシーンに置いた `ReactionSystem` を無効にする（チェックを外す）だけでよい。**
/// ホイールも開かず、通信の受け口も作らない。
///
/// ## つないでいないとき
///
/// タイトル画面など、つないでいない所ではホイールを開かない。
/// 検証シーンでは **Allow Offline** を ON にして、自分の画面だけで出す。
///
/// ## 置き場所
///
/// ロビーのシーンに1つ置く。**マップを移っても消えずに残る**（ロビーへ戻ってきたときに増えた分は消す）。
/// 通信には NetworkObject を使わず「名前付きのメッセージ」で送るので、ネットワーク用のプレハブ一覧に入れなくてよい。
/// </summary>
public class ReactionRelay : MonoBehaviour
{
    private const string SendMessageName = "StarSweepers.Reaction.Send";
    private const string ShowMessageName = "StarSweepers.Reaction.Show";

    /// <summary>いま残っている係。ロビーへ戻ってきたときに2つにならないように使う。</summary>
    public static ReactionRelay Current { get; private set; }

    [Tooltip("リアクションを選ぶ円（同じプレハブの中）")]
    [SerializeField] private ReactionWheel wheel;

    [Tooltip("画面の端に名前とスタンプを出す係（同じプレハブの中）")]
    [SerializeField] private ReactionFeed feed;

    [Tooltip("ON なら、つないでいなくても自分の画面だけで使える（検証シーン用）")]
    [SerializeField] private bool allowOffline;

    [Tooltip("ON なら、シーンを移っても消えずに残る（ロビーに置くとき）")]
    [SerializeField] private bool keepAcrossScenes = true;

    [Tooltip("同じ人が続けて送れる間隔（秒）。連打で画面が埋まらないように、ホストが間引く")]
    [SerializeField] private float minIntervalSeconds = 0.5f;

    /// <summary>受け口を作った通信の係。つなぎ直すと別物になるので、変わったら作り直す。</summary>
    private CustomMessagingManager registeredMessaging;

    /// <summary>ホストが覚えておく、人ごとの最後に受け付けた時刻。</summary>
    private readonly Dictionary<ulong, float> lastAcceptedAt = new Dictionary<ulong, float>();

    private void Awake()
    {
        if (keepAcrossScenes)
        {
            // **ロビーへ戻ってきたとき、前から残っている係があれば、新しいほうを消す**
            if (Current != null && Current != this)
            {
                Destroy(gameObject);
                return;
            }

            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
        }

        Current = this;
    }

    private void OnEnable()
    {
        if (wheel != null)
        {
            wheel.Chosen += OnChosen;
        }
    }

    private void OnDisable()
    {
        if (wheel != null)
        {
            wheel.Chosen -= OnChosen;
            wheel.enabled = false;
        }

        Unregister();
    }

    private void OnDestroy()
    {
        if (Current == this)
        {
            Current = null;
        }
    }

    private void Update()
    {
        NetworkManager manager = NetworkManager.Singleton;
        bool online = manager != null && manager.IsListening && manager.CustomMessagingManager != null;

        // つないでいない所（タイトル画面など）ではホイールを開かない
        if (wheel != null)
        {
            wheel.enabled = online || allowOffline;
        }

        if (!online)
        {
            Unregister();
            return;
        }

        if (registeredMessaging != manager.CustomMessagingManager)
        {
            Unregister();
            Register(manager);
        }
    }

    private void Register(NetworkManager manager)
    {
        registeredMessaging = manager.CustomMessagingManager;
        lastAcceptedAt.Clear();

        if (manager.IsServer)
        {
            registeredMessaging.RegisterNamedMessageHandler(SendMessageName, OnSendReceived);
        }
        else
        {
            registeredMessaging.RegisterNamedMessageHandler(ShowMessageName, OnShowReceived);
        }
    }

    private void Unregister()
    {
        if (registeredMessaging == null)
        {
            return;
        }

        // 通信を止めたあとは、係の中身がもう片付いていることがある
        try
        {
            registeredMessaging.UnregisterNamedMessageHandler(SendMessageName);
            registeredMessaging.UnregisterNamedMessageHandler(ShowMessageName);
        }
        catch (System.Exception)
        {
        }

        registeredMessaging = null;
    }

    /// <summary>自分がホイールで選んだ。</summary>
    private void OnChosen(int index)
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsListening || registeredMessaging == null)
        {
            // つないでいない（検証シーン）：自分の画面だけに出す
            ShowLocal(LocalName(), index);
            return;
        }

        if (manager.IsServer)
        {
            ServerAccept(manager.LocalClientId, index);
            return;
        }

        using (FastBufferWriter writer = new FastBufferWriter(8, Allocator.Temp))
        {
            writer.WriteValueSafe((byte)index);
            registeredMessaging.SendNamedMessage(SendMessageName, NetworkManager.ServerClientId, writer);
        }
    }

    /// <summary>（ホスト）参加者から「○番を選んだ」が届いた。</summary>
    private void OnSendReceived(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte index);
        ServerAccept(senderClientId, index);
    }

    /// <summary>（ホスト）受け付けて、名前を付けて全員へ送り、自分の画面にも出す。</summary>
    private void ServerAccept(ulong senderClientId, int index)
    {
        if (wheel == null || wheel.EntryAt(index) == null)
        {
            return;
        }

        float now = Time.unscaledTime;
        if (lastAcceptedAt.TryGetValue(senderClientId, out float last) && now - last < minIntervalSeconds)
        {
            return;
        }
        lastAcceptedAt[senderClientId] = now;

        NetworkManager manager = NetworkManager.Singleton;
        string senderName = NameOf(senderClientId);

        List<ulong> targets = new List<ulong>();
        foreach (ulong clientId in manager.ConnectedClientsIds)
        {
            if (clientId != manager.LocalClientId)
            {
                targets.Add(clientId);
            }
        }

        if (targets.Count > 0)
        {
            using (FastBufferWriter writer = new FastBufferWriter(256, Allocator.Temp))
            {
                writer.WriteValueSafe((byte)index);
                writer.WriteValueSafe(new FixedString128Bytes(Truncate(senderName)));
                registeredMessaging.SendNamedMessage(ShowMessageName, targets, writer);
            }
        }

        ShowLocal(senderName, index);
    }

    /// <summary>（参加者）ホストから「（名前）が○番」が届いた。</summary>
    private void OnShowReceived(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte index);
        reader.ReadValueSafe(out FixedString128Bytes senderName);
        ShowLocal(senderName.ToString(), index);
    }

    private void ShowLocal(string senderName, int index)
    {
        if (feed != null && wheel != null)
        {
            feed.Show(senderName, wheel.EntryAt(index));
        }
    }

    /// <summary>その人の名前。試合の係（<see cref="SpaceJunkSession"/>）がいればそこから、いなければ仮の名前。</summary>
    private static string NameOf(ulong clientId)
    {
        if (SpaceJunkSession.Current != null)
        {
            return SpaceJunkSession.Current.NameOf(clientId);
        }

        NetworkManager manager = NetworkManager.Singleton;
        return manager != null && clientId == manager.LocalClientId ? LocalName() : $"参加者 {clientId}";
    }

    private static string LocalName()
    {
        string name = GameSettings.PlayerName;
        return string.IsNullOrEmpty(name) ? "あなた" : name;
    }

    /// <summary>送れる長さ（30文字）に収める。名前は16文字までなので、ふつうは切れない。</summary>
    private static string Truncate(string text)
    {
        return text.Length > 30 ? text.Substring(0, 30) : text;
    }
}
