import socket
import numpy as np
from scipy.signal import butter, filtfilt, welch
from scipy.integrate import trapezoid
import time
import threading
import pickle

# =====================================================
# CONFIG
# =====================================================

# OpenBCI GUI UDP stream
UDP_IP = "127.0.0.1"
UDP_PORT = 6677

# Unity TCP listener
TCP_IP = "127.0.0.1"
TCP_PORT = 5005

# Match training: EXG Channel 0, 2, 3, 4, 5, 6
# (Assuming UDP channel indices 0..7 map to EXG 0..7)
CHANNELS_TO_USE = [0, 1, 2, 3, 4, 5, 6]
CHANNEL_COUNT = 8

SAMPLE_RATE = 250
BUFFER_SECONDS = 5
CHUNK_SAMPLES = 500         # 2 seconds @ 250 Hz

# Bandpass settings (match model.py)
LOWCUT = 0.5
HIGHCUT = 30.0
FILTER_ORDER = 4

MODEL_PATH = "/Users/alpaca/Desktop/VRhackathon/MLmodel/fisher_model.pkl"

# =====================================================
# UTILITIES
# =====================================================

def clean_signal(x, clip_val=10.0):
    """
    Clean up a signal:
    - convert to float64 array
    - replace NaN and ±Inf with 0
    - clip to a modest range (optional)
    """
    x = np.asarray(x, dtype=np.float64)
    x = np.nan_to_num(x, nan=0.0, posinf=0.0, neginf=0.0)
    x = np.clip(x, -clip_val, clip_val)
    return x

def butter_bandpass(lowcut, highcut, fs, order=4):
    nyq = 0.5 * fs
    low = lowcut / nyq
    high = highcut / nyq
    b, a = butter(order, [low, high], btype='band')
    return b, a

def bandpass_filter(data):
    """
    Zero-phase bandpass filter, same style as model.py (filtfilt).
    data: 1D numpy array
    """
    data = np.asarray(data)
    if data.size == 0:
        return data
    data = clean_signal(data)
    b, a = butter_bandpass(LOWCUT, HIGHCUT, SAMPLE_RATE, FILTER_ORDER)
    return filtfilt(b, a, data)

def remove_dc_offset(data):
    """
    data: 1D or 2D numpy array
    """
    data = np.asarray(data)
    if data.ndim == 1:
        return data - np.mean(data)
    else:
        return data - np.mean(data, axis=0, keepdims=True)

# =====================================================
# LOAD FISHER MODEL
# =====================================================

with open(MODEL_PATH, "rb") as f:
    saved = pickle.load(f)

w = saved["w"]
threshold = saved["threshold"]
extra = saved["extra"]

BANDS = extra["bands"]
MODEL_SR = extra["sample_rate"]
MODEL_CHUNK = extra["chunk_samples"]

if MODEL_SR != SAMPLE_RATE:
    print(f"WARNING: model trained at {MODEL_SR} Hz, streaming at {SAMPLE_RATE} Hz")

if MODEL_CHUNK != CHUNK_SAMPLES:
    print(f"WARNING: model trained on {MODEL_CHUNK} samples, streaming with {CHUNK_SAMPLES} samples")

# =====================================================
# FEATURE / CLASSIFIER FUNCTIONS
# =====================================================

def compute_bandpowers(chunk):
    """
    chunk: 1D array, already filtered + DC-removed.
    Returns RELATIVE band powers (sum to 1), matching model.py.
    """
    chunk = np.asarray(chunk)
    if chunk.size == 0:
        return np.zeros(len(BANDS))

    chunk = clean_signal(chunk)

    freqs, psd = welch(chunk, fs=SAMPLE_RATE, nperseg=len(chunk))
    psd = clean_signal(psd)

    band_powers = []
    for fmin, fmax in BANDS.values():
        mask = (freqs >= fmin) & (freqs <= fmax)
        if not np.any(mask):
            band_powers.append(0.0)
            continue
        band_psd = psd[mask]
        power = trapezoid(band_psd, freqs[mask])
        band_powers.append(power)

    band_powers = np.array(band_powers, dtype=np.float64)
    total = np.sum(band_powers)
    if total <= 0:
        return np.zeros_like(band_powers)

    # Relative band powers = same "regularization" as training
    return band_powers / total

def classify(chunk):
    """
    chunk: 1D array (500 samples) after filtering & DC removal.
    Returns 0 (non-confusion) or 1 (confusion).
    """
    feats = compute_bandpowers(chunk)
    score = feats.dot(w)
    return int(score > threshold)

# =====================================================
# ROLLING BUFFER
# =====================================================

buffer_len = BUFFER_SECONDS * SAMPLE_RATE
rolling_buffer = np.zeros(buffer_len, dtype=np.float64)

# =====================================================
# UDP RECEIVER (OpenBCI → Python)
# =====================================================

def udp_receiver():
    global rolling_buffer

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.bind((UDP_IP, UDP_PORT))
    print(f"Listening for OpenBCI UDP on {UDP_IP}:{UDP_PORT}...")

    while True:
        data, addr = sock.recvfrom(4096)
        samples = np.frombuffer(data, dtype=np.float32)

        if samples.size == 0:
            continue
        if len(samples) % CHANNEL_COUNT != 0:
            # malformed packet
            continue

        # reshape to (num_samples, CHANNEL_COUNT)
        samples = samples.reshape(-1, CHANNEL_COUNT)

        # select same channels as training
        selected = samples[:, CHANNELS_TO_USE]   # (n_samples, len(CHANNELS_TO_USE))

        # average across channels
        averaged = selected.mean(axis=1)         # (n_samples,)

        # NO min-max session normalization anymore,
        # just basic cleaning before bandpass in the classifier
        averaged_clean = clean_signal(averaged)

        # push into rolling buffer
        for val in averaged_clean:
            rolling_buffer = np.roll(rolling_buffer, -1)
            rolling_buffer[-1] = val

# =====================================================
# TCP SENDER (Python → Unity)
# =====================================================

def tcp_sender():
    global rolling_buffer

    server_sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server_sock.bind((TCP_IP, TCP_PORT))
    server_sock.listen(1)
    print(f"Waiting for Unity TCP connection on {TCP_IP}:{TCP_PORT}...")
    conn, addr = server_sock.accept()
    print("Unity connected:", addr)

    print("Waiting 5 seconds before starting classification...")
    time.sleep(5)

    try:
        while True:
            if rolling_buffer.size < CHUNK_SAMPLES:
                time.sleep(0.01)
                continue

            # last 500 samples (2 seconds)
            window = rolling_buffer[-CHUNK_SAMPLES:]

            # match training: bandpass → DC removal
            window_filtered = bandpass_filter(window)
            window_processed = remove_dc_offset(window_filtered)

            if (
                window_processed.size == 0
                or np.allclose(window_processed, 0)
                or np.isnan(window_processed).any()
                or np.isinf(window_processed).any()
            ):
                pred = 0
            else:
                pred = classify(window_processed)

            msg = str(pred) + "\n"
            conn.sendall(msg.encode())

            # classify every 20 samples
            time.sleep(20 / SAMPLE_RATE)

    except Exception as e:
        print("Error in tcp_sender:", e)

    finally:
        conn.close()
        server_sock.close()

# =====================================================
# MAIN
# =====================================================

if __name__ == "__main__":
    udp_thread = threading.Thread(target=udp_receiver, daemon=True)
    tcp_thread = threading.Thread(target=tcp_sender, daemon=True)

    udp_thread.start()
    tcp_thread.start()

    print("Press Ctrl+C to exit...")
    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        print("Exiting...")
