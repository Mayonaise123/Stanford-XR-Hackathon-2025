import numpy as np
import pandas as pd
from scipy.signal import butter, filtfilt, welch
from scipy.integrate import trapezoid
from sklearn.model_selection import train_test_split
from sklearn.metrics import accuracy_score, confusion_matrix, classification_report
import pickle

# ================== USER SETTINGS ==================
CSV_CONFUSION = "/Users/alpaca/Documents/OpenBCI_GUI/Recordings/confusion/combined_eeg_microvolts.csv"
CSV_NONCONFUSION = "/Users/alpaca/Documents/OpenBCI_GUI/Recordings/not confusion/combined_eeg_microvolts.csv"

SAMPLE_RATE = 250
CHUNK_SAMPLES = 500       # 2 seconds
TRAIN_SIZE = 0.70
RANDOM_SEED = 40

# Frequency bands
BANDS = {
    "delta": (1.0, 4.0),
    "theta": (4.0, 8.0),
    "alpha": (8.0,13.0),
    "beta":  (13.0,30.0)
}

# Bandpass filter
HPF = 0.5
LPF = 30.0
FILTER_ORDER = 4

MODEL_SAVE_PATH = "/Users/alpaca/Desktop/VRhackathon/MLmodel/fisher_model.pkl"

# ======================================================
#                SIGNAL PROCESSING
# ======================================================

def butter_bandpass_filter(data, lowcut, highcut, fs, order=4):
    """
    data: 1D numpy array
    """
    nyq = 0.5 * fs
    low = lowcut / nyq
    high = highcut / nyq
    b, a = butter(order, [low, high], btype="band")
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

def load_csv_all_exg(path):
    """
    Load all EXG channels from a CSV and return as (samples, n_channels) float array.
    """
    df = pd.read_csv(path)
    df.columns = [c.strip() for c in df.columns]
    exg_cols = [c for c in df.columns if c.startswith("EXG Channel")]
    if not exg_cols:
        raise ValueError(f"No EXG Channel columns found in {path}. Columns: {df.columns}")
    data = df[exg_cols].astype(float).values
    return data, exg_cols

def load_and_avg(path):
    """
    Load EXG channels, average across them to get a single 1D signal.
    """
    data, cols = load_csv_all_exg(path)   # (samples, n_channels)
    avg = data.mean(axis=1)               # shape (samples,)
    return avg, cols

# ======================================================
#      RELATIVE BANDPOWER (REPLACES OLD NORMALIZATION)
# ======================================================

def compute_bandpowers(chunk_1d, fs, bands):
    """
    Computes RELATIVE band powers (sum to 1).
    Independent of amplitude / units / scaling.
    """
    freqs, psd = welch(chunk_1d, fs=fs, nperseg=len(chunk_1d))
    band_powers = []

    for fmin, fmax in bands.values():
        mask = (freqs >= fmin) & (freqs <= fmax)
        if not np.any(mask):
            band_powers.append(0.0)
            continue
        p = trapezoid(psd[mask], freqs[mask])
        band_powers.append(p)

    band_powers = np.array(band_powers, dtype=np.float64)
    total = band_powers.sum()

    if total <= 0:
        return np.zeros_like(band_powers)

    return band_powers / total      # relative (sum=1)

def chunk_and_spectral_features(signal_1d, chunk_size, fs, bands):
    """
    signal_1d: 1D array (continuous signal)
    Returns: (n_chunks, n_features) array of bandpowers.
    """
    n_samples = len(signal_1d)
    n_chunks = n_samples // chunk_size
    feature_list = []

    for i in range(n_chunks):
        start = i * chunk_size
        end = start + chunk_size
        chunk = signal_1d[start:end]
        feature_list.append(compute_bandpowers(chunk, fs, bands))

    return np.vstack(feature_list)

# ======================================================
#           FISHER LINEAR DISCRIMINANT
# ======================================================

class FisherLinearDiscriminant:
    def fit(self, X, y):
        X0 = X[y == 0]
        X1 = X[y == 1]
        m0 = X0.mean(axis=0)
        m1 = X1.mean(axis=0)

        S0 = np.cov(X0, rowvar=False) * (len(X0) - 1)
        S1 = np.cov(X1, rowvar=False) * (len(X1) - 1)
        Sw = S0 + S1

        self.w = np.linalg.pinv(Sw).dot(m1 - m0)
        self.threshold = 0.5 * (self.w.dot(m0) + self.w.dot(m1))

    def predict(self, X):
        scores = X.dot(self.w)
        return (scores > self.threshold).astype(int)

    def save(self, filename, extra_info={}):
        with open(filename, "wb") as f:
            pickle.dump({
                "w": self.w,
                "threshold": self.threshold,
                "extra": extra_info
            }, f)

    def load(self, filename):
        with open(filename, "rb") as f:
            data = pickle.load(f)
        self.w = data["w"]
        self.threshold = data["threshold"]
        self.extra = data["extra"]

# ======================================================
#                  DATASET PREPARATION
# ======================================================

def prepare_dataset_fixed(path, label):
    """
    Load, average channels, bandpass filter,
    DC remove, chunk, and extract RELATIVE bandpowers.
    """
    raw_avg, cols = load_and_avg(path)            # 1D signal

    # NO min-max normalization anymore
    filtered = butter_bandpass_filter(raw_avg, HPF, LPF, SAMPLE_RATE, FILTER_ORDER)
    filtered = remove_dc_offset(filtered)

    feats = chunk_and_spectral_features(filtered, CHUNK_SAMPLES, SAMPLE_RATE, BANDS)
    labels = np.full((feats.shape[0],), label)

    return feats, labels

# ======================================================
#                     MAIN
# ======================================================

def main():

    print("Preparing confusion dataset...")
    X_conf, y_conf = prepare_dataset_fixed(CSV_CONFUSION, 1)

    print("Preparing non-confusion dataset...")
    X_non, y_non = prepare_dataset_fixed(CSV_NONCONFUSION, 0)

    X = np.vstack([X_conf, X_non])
    y = np.concatenate([y_conf, y_non])

    X_train, X_test, y_train, y_test = train_test_split(
        X, y,
        train_size=TRAIN_SIZE,
        shuffle=True,
        stratify=y,
        random_state=RANDOM_SEED
    )

    clf = FisherLinearDiscriminant()
    clf.fit(X_train, y_train)

    y_pred = clf.predict(X_test)

    print("\n=== RESULTS ===")
    print("Accuracy:", accuracy_score(y_test, y_pred))
    print("Confusion Matrix:\n", confusion_matrix(y_test, y_pred))
    print("Classification Report:\n", classification_report(y_test, y_pred))

    # ---- SAVE MODEL ----
    clf.save(MODEL_SAVE_PATH, extra_info={
        "bands": BANDS,
        "sample_rate": SAMPLE_RATE,
        "chunk_samples": CHUNK_SAMPLES
    })

    print(f"\nModel saved to {MODEL_SAVE_PATH}")

if __name__ == "__main__":
    main()
