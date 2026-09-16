"""WebGL ビルドをローカルで開くための簡易サーバー。

Gzip 圧縮のまま出力した Unity の WebGL ビルドは、
サーバーが「Content-Encoding: gzip」を返さないとブラウザが展開できない。
（Decompression Fallback をオフにしているため、Unity 側では展開しない）
python -m http.server はこのヘッダを付けないので、ここで付ける。

使い方：
    python tools/serve_webgl.py
    → http://localhost:8000/ をブラウザで開く

止めるときは Ctrl+C。
"""

import argparse
import http.server
import os
import socketserver

DEFAULT_PORT = 8000
DEFAULT_ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Build", "WebGL")

# 圧縮前の中身に合わせた種類を返す（.gz を外した拡張子で判断する）
CONTENT_TYPES = {
    ".js": "application/javascript",
    ".wasm": "application/wasm",
    ".data": "application/octet-stream",
    ".symbols.json": "application/json",
}


class GzipAwareHandler(http.server.SimpleHTTPRequestHandler):
    def end_headers(self):
        path = self.path.split("?")[0]
        if path.endswith(".gz"):
            self.send_header("Content-Encoding", "gzip")
        # 開発中はキャッシュを残さない（作り直した版がすぐ反映されるように）
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def guess_type(self, path):
        if path.endswith(".gz"):
            stripped = path[: -len(".gz")]
            for suffix, content_type in CONTENT_TYPES.items():
                if stripped.endswith(suffix):
                    return content_type
            return "application/octet-stream"
        return super().guess_type(path)


def main():
    parser = argparse.ArgumentParser(description="WebGL ビルドをローカルで開くための簡易サーバー")
    parser.add_argument("--port", type=int, default=DEFAULT_PORT, help="使うポート（既定 8000）")
    parser.add_argument("--root", default=DEFAULT_ROOT, help="配信するフォルダ（既定 Build/WebGL）")
    args = parser.parse_args()

    root = os.path.abspath(args.root)
    if not os.path.isfile(os.path.join(root, "index.html")):
        raise SystemExit("index.html が見つかりません: " + root)

    os.chdir(root)
    socketserver.TCPServer.allow_reuse_address = True
    with socketserver.TCPServer(("", args.port), GzipAwareHandler) as httpd:
        print("配信するフォルダ: " + root)
        print("ブラウザで http://localhost:" + str(args.port) + "/ を開いてください（Ctrl+C で終了）")
        httpd.serve_forever()


if __name__ == "__main__":
    main()
