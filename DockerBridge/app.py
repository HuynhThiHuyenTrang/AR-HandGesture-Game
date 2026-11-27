import os
import time
import threading
from typing import List
from fastapi import FastAPI, Request, Response
from pydantic import BaseModel
import paho.mqtt.client as mqtt

AIO_USERNAME = os.getenv("ADAFRUIT_AIO_USERNAME", "HuyenTrang")
AIO_KEY = os.getenv("ADAFRUIT_AIO_KEY", "")
MQTT_BROKER = "io.adafruit.com"
MQTT_PORT = 1883

CONTROL_FEEDS = ["set-level", "pause-game", "reset-game"]

app = FastAPI(title="AR Monster Hunter - Docker Adafruit IO 2-Way Bridge")

state_lock = threading.Lock()
control_state = {
    "set_level": 1,
    "pause_game": 0,
    "reset_ts": "",
    "sync_started": False,
    "mqtt_connected": False,
}
telemetry_cache = {}
camera_lock = threading.Lock()
latest_camera_bytes: bytes = b""
latest_camera_id: int = 0
latest_camera_ts: float = 0.0
latest_camera_rot: int = 0


def on_connect(client, userdata, flags, reason_code, properties=None):
    connected = (reason_code == 0)
    with state_lock:
        control_state["mqtt_connected"] = connected
    print(f"[MQTT] Connected={connected} (rc={reason_code})")
    if connected:
        for fk in CONTROL_FEEDS:
            topic = f"{AIO_USERNAME}/feeds/{fk}"
            client.subscribe(topic)
            print(f"[MQTT] Subscribed: {topic}")


def on_disconnect(client, userdata, disconnect_flags, reason_code, properties=None):
    with state_lock:
        control_state["mqtt_connected"] = False
    print(f"[MQTT] Disconnected (rc={reason_code})")


def on_message(client, userdata, msg):
    topic = msg.topic
    val = msg.payload.decode("utf-8", errors="ignore").strip()
    feed_key = topic.split("/")[-1]
    print(f"[MQTT <- Adafruit] {feed_key} = {val}")

    with state_lock:
        if feed_key == "set-level":
            try:
                lvl = int(val)
                if 1 <= lvl <= 5:
                    control_state["set_level"] = lvl
            except ValueError:
                pass
        elif feed_key == "pause-game":
            try:
                control_state["pause_game"] = 1 if int(val) == 1 else 0
            except ValueError:
                pass
        elif feed_key == "reset-game":
            control_state["reset_ts"] = f"{time.time():.3f}"


mqtt_client = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2)
mqtt_client.username_pw_set(AIO_USERNAME, AIO_KEY)
mqtt_client.on_connect = on_connect
mqtt_client.on_disconnect = on_disconnect
mqtt_client.on_message = on_message
mqtt_client.reconnect_delay_set(min_delay=1, max_delay=15)
mqtt_client.connect_async(MQTT_BROKER, MQTT_PORT, keepalive=60)
mqtt_client.loop_start()


class FeedItem(BaseModel):
    key: str
    value: str


class TelemetryBatch(BaseModel):
    feeds: List[FeedItem]


@app.get("/health")
def health():
    with state_lock:
        return {
            "status": "ok",
            "mqtt_connected": control_state["mqtt_connected"],
            "control": dict(control_state),
            "telemetry": dict(telemetry_cache),
        }


@app.get("/control")
def get_control():
    with state_lock:
        return dict(control_state)


@app.post("/telemetry")
def post_telemetry(batch: TelemetryBatch):
    published = []
    for item in batch.feeds:
        topic = f"{AIO_USERNAME}/feeds/{item.key}"
        mqtt_client.publish(topic, item.value)
        with state_lock:
            telemetry_cache[item.key] = item.value
        published.append(item.key)
    print(f"[Unity -> MQTT] Published: {published}")
    return {"status": "ok", "published": published}


@app.post("/camera_frame")
async def post_camera_frame(request: Request, rot: int = 0):
    global latest_camera_bytes, latest_camera_id, latest_camera_ts, latest_camera_rot
    data = await request.body()
    if not data:
        return {"status": "empty"}
    with camera_lock:
        latest_camera_bytes = data
        latest_camera_id += 1
        latest_camera_ts = time.time()
        latest_camera_rot = rot
    return {"status": "ok", "frame_id": latest_camera_id}


@app.post("/sync_start")
def post_sync_start(started: bool = True):
    with state_lock:
        control_state["sync_started"] = started
    return {"status": "ok", "sync_started": started}


@app.get("/camera_frame")
def get_camera_frame(last_id: int = 0):
    with camera_lock:
        if not latest_camera_bytes or latest_camera_id <= last_id or (time.time() - latest_camera_ts) > 3.0:
            return Response(status_code=204)
        data = latest_camera_bytes
        fid = latest_camera_id
        rot = latest_camera_rot
    return Response(content=data, media_type="image/jpeg", headers={"X-Frame-Id": str(fid), "X-Cam-Rot": str(rot)})
