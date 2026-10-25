"""本机机器人协议调试服务端；只记录产品类型，不执行运动控制。"""
import argparse
import json
import socket
import socketserver
from datetime import datetime


class Handler(socketserver.StreamRequestHandler):
    def handle(self):
        print(f"已连接 {self.client_address}", flush=True)
        try:
            while True:
                line = self.rfile.readline(1024 * 1024 + 1)
                if not line:
                    break
                if len(line) > 1024 * 1024 or not line.endswith(b"\n"):
                    print("报文超过上限或未正确分帧，关闭连接", flush=True)
                    break
                try:
                    message = json.loads(line.decode("utf-8"))
                    if message.get("type") != "product_recognized" or message.get("version") != 1:
                        print("忽略未知报文", flush=True)
                        continue
                    print(f"{datetime.now().isoformat(timespec='milliseconds')} "
                          f"类型={message['productType']} ID={message['productId']} "
                          f"run={message['runId']} seq={message['sequence']} "
                          f"面积={message['areaPx2']} 周长={message['perimeterPx']}", flush=True)
                except (UnicodeError, ValueError, KeyError, TypeError) as error:
                    print(f"无效报文: {error}", flush=True)
        except (OSError, socket.error) as error:
            print(f"连接中断: {error}", flush=True)
        finally:
            print(f"已断开 {self.client_address}", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=5000)
    options = parser.parse_args()
    if not 1 <= options.port <= 65535:
        parser.error("端口须为1～65535")
    class Server(socketserver.ThreadingTCPServer):
        allow_reuse_address = True
        daemon_threads = True
    with Server((options.host, options.port), Handler) as server:
        print(f"模拟机器人监听 {options.host}:{options.port}，Ctrl+C 停止。只打印报文，不控制设备。", flush=True)
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass
