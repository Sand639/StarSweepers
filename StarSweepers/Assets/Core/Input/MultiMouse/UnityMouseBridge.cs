using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectEL4S.MultiMouse
{
    /// <summary>
    /// Raw Input で受け取ったマウスの移動量を、Unity の <see cref="Mouse.current"/> の delta へ流し直す橋渡し。
    ///
    /// こちらがマウスの Raw Input を登録している間は、Unity の Mouse.current.delta が 0 のままになる
    /// （2026/10/6 に検証）。カメラを回すスクリプトなどを動かし続けるために、全部のマウスの移動量を足して書き込む。
    ///
    /// クリック・カーソル位置・ホイールは、Windows の別の経路で Unity に届き続けるので、
    /// ここでは扱わない（扱うと二重になる）。
    /// </summary>
    internal sealed class UnityMouseBridge
    {
        private Vector2 _delta;

        /// <summary>Raw Input の生の移動量（Y は下が正）をそのまま渡す。</summary>
        public void Add(int rawDeltaX, int rawDeltaY)
        {
            // Unity の delta は上が正。Unity も Raw Input の生のカウントをそのまま使っているので倍率は掛けない
            _delta += new Vector2(rawDeltaX, -rawDeltaY);
        }

        public void Clear()
        {
            _delta = Vector2.zero;
        }

        /// <summary>
        /// 溜まった移動量を Unity のマウスへ書き込む。
        /// Input System が入力を処理する直前（onBeforeUpdate）に呼ぶと、同じフレームのうちに反映される。
        /// delta は Unity が毎フレーム 0 に戻すので、こちらで戻す必要はない。
        /// </summary>
        public void Flush()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.enabled || _delta == Vector2.zero)
            {
                _delta = Vector2.zero;
                return;
            }
            InputSystem.QueueDeltaStateEvent(mouse.delta, _delta);
            _delta = Vector2.zero;
        }
    }
}
