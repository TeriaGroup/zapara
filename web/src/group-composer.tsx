import { FormEvent, useEffect, useRef, useState } from "react";
import { Icon } from "./icons";
import { groupMediaLimit, groupVoiceLimit, recordingFilename } from "./group-media";

type RecordingKind = "voice" | "circle";
type AttachmentKind = "image" | "video" | "file";
type Capture = {
  kind: RecordingKind;
  recorder: MediaRecorder;
  stream: MediaStream;
  chunks: Blob[];
  bytes: number;
  startedAt: number;
  timer: number | null;
  send: boolean;
  failed: boolean;
};

const voiceTypes = ["audio/webm;codecs=opus", "audio/webm", "audio/ogg;codecs=opus", "audio/mp4"];
const circleTypes = ["video/webm;codecs=vp8,opus", "video/webm;codecs=vp9,opus", "video/webm", "video/mp4"];

function clock(ms: number) {
  const seconds = Math.max(0, Math.floor(ms / 1000));
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")}`;
}

export function GroupComposer({ draft, editing, replyTo, contextText, allowMedia, onDraft, onSubmit, onCancelContext, onChoose, onRecorded, onError }: {
  draft: string;
  editing: boolean;
  replyTo: boolean;
  contextText: string;
  allowMedia: boolean;
  onDraft: (value: string) => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => void;
  onCancelContext: () => void;
  onChoose: (kind: AttachmentKind) => void;
  onRecorded: (kind: RecordingKind, name: string, blob: Blob, durationMs: number) => Promise<void>;
  onError: (message: string) => void;
}) {
  const [panel, setPanel] = useState(false);
  const [recording, setRecording] = useState<RecordingKind | null>(null);
  const [starting, setStarting] = useState(false);
  const [sending, setSending] = useState(false);
  const [elapsed, setElapsed] = useState(0);
  const active = useRef<Capture | null>(null);
  const preview = useRef<HTMLVideoElement>(null);
  const mounted = useRef(false);
  const requestEpoch = useRef(0);
  const startingRef = useRef(false);
  const canRecord = useRef(allowMedia && !editing);
  canRecord.current = allowMedia && !editing;

  function release(capture: Capture) {
    if (capture.timer !== null) window.clearInterval(capture.timer);
    capture.stream.getTracks().forEach(track => track.stop());
    if (preview.current) preview.current.srcObject = null;
    if (active.current === capture) active.current = null;
  }

  function stop(send: boolean) {
    const capture = active.current;
    if (!capture) return;
    capture.send = send;
    if (capture.recorder.state !== "inactive") capture.recorder.stop();
  }

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
      requestEpoch.current += 1;
      const capture = active.current;
      if (capture) {
        capture.send = false;
        if (capture.recorder.state !== "inactive") capture.recorder.stop();
        release(capture);
      }
    };
  }, []);

  useEffect(() => {
    if (!allowMedia || editing) {
      requestEpoch.current += 1;
      setPanel(false);
      stop(false);
    }
  }, [allowMedia, editing]);

  useEffect(() => {
    if (recording !== "circle" || !preview.current || !active.current) return;
    preview.current.srcObject = active.current.stream;
    void preview.current.play().catch(() => undefined);
  }, [recording]);

  async function begin(kind: RecordingKind) {
    if (active.current || startingRef.current || sending || !allowMedia || editing) return;
    if (!navigator.mediaDevices?.getUserMedia || typeof MediaRecorder === "undefined" || typeof MediaRecorder.isTypeSupported !== "function") {
      onError(kind === "voice" ? "Этот браузер не записывает голос" : "Этот браузер не снимает кружочки");
      return;
    }
    startingRef.current = true;
    setStarting(true);
    setPanel(false);
    const epoch = ++requestEpoch.current;
    let stream: MediaStream | null = null;
    try {
      stream = await navigator.mediaDevices.getUserMedia(kind === "voice" ? { audio: true } : {
        audio: true,
        video: { facingMode: "user", width: { ideal: 480, max: 640 }, height: { ideal: 480, max: 640 }, frameRate: { ideal: 24, max: 30 } },
      });
      if (!mounted.current || requestEpoch.current !== epoch || !canRecord.current) {
        stream.getTracks().forEach(track => track.stop());
        return;
      }
      const mime = (kind === "voice" ? voiceTypes : circleTypes).find(value => MediaRecorder.isTypeSupported(value));
      if (!mime) throw new Error("unsupported format");
      const recorder = new MediaRecorder(stream, kind === "voice"
        ? { mimeType: mime, audioBitsPerSecond: 24_000 }
        : { mimeType: mime, videoBitsPerSecond: 450_000, audioBitsPerSecond: 24_000 });
      recordingFilename(kind, recorder.mimeType || mime);
      const capture: Capture = { kind, recorder, stream, chunks: [], bytes: 0, startedAt: Date.now(), timer: null, send: false, failed: false };
      active.current = capture;
      recorder.ondataavailable = event => {
        if (!event.data.size) return;
        capture.chunks.push(event.data);
        capture.bytes += event.data.size;
        if (capture.bytes > (kind === "voice" ? groupVoiceLimit : groupMediaLimit)) {
          capture.send = false;
          if (!capture.failed && mounted.current) onError("Запись слишком большая");
          capture.failed = true;
          if (recorder.state !== "inactive") recorder.stop();
        }
      };
      recorder.onerror = () => {
        capture.send = false;
        if (!capture.failed && mounted.current) onError("Запись прервалась");
        capture.failed = true;
        if (recorder.state !== "inactive") recorder.stop();
      };
      recorder.onstop = () => {
        release(capture);
        if (!mounted.current) return;
        setRecording(null);
        if (!capture.send || capture.failed) return;
        const blob = new Blob(capture.chunks, { type: recorder.mimeType || mime });
        if (blob.size < (kind === "voice" ? 200 : 1000)) {
          onError(kind === "voice" ? "Слишком короткое сообщение" : "Слишком короткий кружок");
          return;
        }
        if (blob.size > (kind === "voice" ? groupVoiceLimit : groupMediaLimit)) { onError("Запись слишком большая"); return; }
        const durationMs = Math.max(1, Math.min(kind === "voice" ? 180_000 : 60_000, Date.now() - capture.startedAt));
        setSending(true);
        void onRecorded(kind, recordingFilename(kind, blob.type), blob, durationMs)
          .catch(() => { if (mounted.current) onError("Сообщение не отправилось"); })
          .finally(() => { if (mounted.current) setSending(false); });
      };
      recorder.start(250);
      capture.timer = window.setInterval(() => {
        const milliseconds = Date.now() - capture.startedAt;
        if (mounted.current) setElapsed(milliseconds);
        if (milliseconds >= (kind === "voice" ? 180_000 : 60_000) && recorder.state !== "inactive") stop(true);
      }, 200);
      setElapsed(0);
      setRecording(kind);
    } catch {
      const capture = active.current;
      if (capture) release(capture);
      if (stream) stream.getTracks().forEach(track => track.stop());
      if (mounted.current) onError(kind === "voice" ? "Нет доступа к микрофону или формат записи не поддерживается" : "Нет доступа к камере или формат записи не поддерживается");
    } finally {
      startingRef.current = false;
      if (mounted.current) setStarting(false);
    }
  }

  if (recording) return (
    <div className="group-recording">
      {recording === "circle" && <div className="circle live"><video ref={preview} muted playsInline autoPlay /><span className="time">{clock(elapsed)}</span></div>}
      <div className="compose">
        {recording === "voice" && <span>Запись {clock(elapsed)}</span>}
        <button className="btn" type="button" onClick={() => stop(false)}>Отменить</button>
        <button className="btn primary" type="button" onClick={() => stop(true)}>Отправить</button>
      </div>
    </div>
  );

  return (
    <>
      {(editing || replyTo) && <div className="compose-context">
        <span className="compose-context-text">{editing ? "Редактирование" : "Ответ"}: {contextText}</span>
        <button className="btn tool" type="button" aria-label={editing ? "Отменить редактирование" : "Отменить ответ"} onClick={onCancelContext}>Отменить</button>
      </div>}
      <form className="compose" onSubmit={onSubmit}>
        {!editing && allowMedia && <button className="btn tool" type="button" aria-label="Вложения" disabled={starting || sending}
          onClick={() => setPanel(value => !value)}><Icon name="paperclip" size={18} /></button>}
        <input value={draft} onChange={event => onDraft(event.target.value)} placeholder="Сообщение" aria-label="Сообщение" maxLength={2000} />
        {draft.trim() || editing || !allowMedia
          ? <button className="btn primary" type="submit" disabled={!draft.trim() || sending}>{editing ? "Сохранить" : "Отправить"}</button>
          : <>
              <button className="btn tool" type="button" aria-label="Кружок" disabled={starting || sending} onClick={() => void begin("circle")}><Icon name="circle" size={18} /></button>
              <button className="btn primary tool" type="button" aria-label="Голосовое" disabled={starting || sending} onClick={() => void begin("voice")}><Icon name="mic" size={18} /></button>
            </>}
      </form>
      {sending && <p className="muted">Отправка записи…</p>}
      {panel && allowMedia && !editing && <div className="actions group-attachment-menu">
        <button type="button" onClick={() => { setPanel(false); onChoose("image"); }}>Фото</button>
        <button type="button" onClick={() => { setPanel(false); onChoose("video"); }}>Видео</button>
        <button type="button" onClick={() => { setPanel(false); onChoose("file"); }}>Документ</button>
        <button type="button" onClick={() => void begin("circle")}>Кружок</button>
      </div>}
    </>
  );
}
