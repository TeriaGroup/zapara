import { useEffect, useRef, useState } from "react";
import * as api from "./api";
import type { GroupMediaDownload } from "./group-media";

function clock(ms: number) {
  const seconds = Math.max(0, Math.round(ms / 1000));
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")}`;
}

function VoiceNote({ src, onError }: { src: string; onError: () => void }) {
  const audio = useRef<HTMLAudioElement>(null);
  const [playing, setPlaying] = useState(false);
  const [duration, setDuration] = useState(0);
  return (
    <div className="voice">
      <button className="btn" type="button" onClick={() => {
        const node = audio.current;
        if (!node) return;
        if (playing) node.pause();
        else void node.play().catch(onError);
      }}>{playing ? "Пауза" : "Слушать"}</button>
      <span>{clock(duration)}</span>
      <audio ref={audio} src={src} preload="metadata" onError={onError} onPlay={() => setPlaying(true)} onPause={() => setPlaying(false)}
        onEnded={() => setPlaying(false)} onLoadedMetadata={event => {
          if (Number.isFinite(event.currentTarget.duration)) setDuration(event.currentTarget.duration * 1000);
        }} />
    </div>
  );
}

function CircleNote({ src, onError }: { src: string; onError: () => void }) {
  const video = useRef<HTMLVideoElement>(null);
  const [playing, setPlaying] = useState(false);
  const [progress, setProgress] = useState(0);
  const [duration, setDuration] = useState(0);
  const radius = 96;
  const length = 2 * Math.PI * radius;
  return (
    <div className="circle">
      <svg className="ring" viewBox="0 0 200 200" aria-hidden="true">
        <circle cx="100" cy="100" r={radius} />
        <circle className="progress" cx="100" cy="100" r={radius} strokeDasharray={length} strokeDashoffset={length * (1 - progress)} />
      </svg>
      <video ref={video} src={src} playsInline preload="metadata" onError={onError} onPlay={() => setPlaying(true)} onPause={() => setPlaying(false)}
        onEnded={() => { setPlaying(false); setProgress(0); }}
        onTimeUpdate={event => { const node = event.currentTarget; if (node.duration) setProgress(node.currentTime / node.duration); }}
        onLoadedMetadata={event => { if (Number.isFinite(event.currentTarget.duration)) setDuration(event.currentTarget.duration * 1000); }} />
      <button className="circle-hit" type="button" aria-label={playing ? "Пауза" : "Смотреть кружок"} onClick={() => {
        const node = video.current;
        if (!node) return;
        if (playing) node.pause();
        else { node.muted = false; void node.play().catch(onError); }
      }} />
      {!playing && <span className="play">▶</span>}
      <span className="time">{clock(duration)}</span>
    </div>
  );
}

export function GroupInlineMedia({ download, busy, onDownload }: {
  download: GroupMediaDownload;
  busy: boolean;
  onDownload: () => void;
}) {
  const container = useRef<HTMLDivElement>(null);
  const [visible, setVisible] = useState(false);
  const [source, setSource] = useState<string | null>(null);
  const [error, setError] = useState(false);

  useEffect(() => {
    const node = container.current;
    if (!node || typeof IntersectionObserver === "undefined") { setVisible(true); return; }
    const observer = new IntersectionObserver(entries => {
      if (entries.some(entry => entry.isIntersecting)) { setVisible(true); observer.disconnect(); }
    }, { rootMargin: "250px" });
    observer.observe(node);
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    if (!visible) return;
    let active = true;
    let objectUrl: string | null = null;
    void api.groupMedia(download).then(blob => {
      if (!active) return;
      if (!blob.size || blob.type === "application/octet-stream") throw new Error("unsupported media");
      objectUrl = URL.createObjectURL(blob);
      setSource(objectUrl);
    }).catch(() => { if (active) setError(true); });
    return () => { active = false; if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [visible, download.href, download.kind, download.filename]);

  return (
    <div className="group-inline-media" ref={container}>
      {!source && !error && <span className="muted">{visible ? "Загрузка медиа…" : "Медиа"}</span>}
      {error && <span className="muted">Не удалось показать медиа</span>}
      {source && !error && download.kind === "image" && <a href={source} target="_blank" rel="noreferrer"><img src={source} alt="Фото" loading="lazy" onError={() => setError(true)} /></a>}
      {source && !error && download.kind === "video" && <video className="group-video" src={source} controls playsInline preload="metadata" onError={() => setError(true)} />}
      {source && !error && download.kind === "voice" && <VoiceNote src={source} onError={() => setError(true)} />}
      {source && !error && download.kind === "circle" && <CircleNote src={source} onError={() => setError(true)} />}
      <button className="group-media-download" type="button" disabled={busy} onClick={onDownload}>{busy ? "Загрузка…" : download.label}</button>
    </div>
  );
}
