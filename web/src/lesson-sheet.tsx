import { Link } from "react-router-dom";
import { Icon } from "./icons";
import { Sheet } from "./sheet";
import { ShareMenu } from "./share";
import type { Lesson } from "./types";

/** Лист пары (#13): по нажатию на карточку — Карта, Домашка, Обсудить в чате группы; вместо отдельных «В чат» и «Обсудить». */
export function LessonSheet({ lesson, dateLabel, mapHref, homeworkHref, chatHref, dayHref, share, onClose }: {
  lesson: Lesson; dateLabel: string; mapHref: string | null; homeworkHref: string; chatHref: string; dayHref?: string; share: string | null; onClose: () => void;
}) {
  const room = (lesson.roomRaw || lesson.classroomRaw || "").trim();
  return <Sheet title={lesson.subjectRaw} onClose={onClose}>
    <div className="stack lesson-sheet">
      <p className="lesson-sheet-meta">{[dateLabel, `${lesson.timeStart}–${lesson.timeEnd}`, room, lesson.teacherRaw?.trim()].filter(Boolean).join(" · ")}</p>
      <div className="lesson-sheet-actions">
        {mapHref ? <Link className="btn primary" to={mapHref}><Icon name="map" size={16} />Карта</Link>
          : <button className="btn primary" type="button" disabled title="Аудитория не указана"><Icon name="map" size={16} />Карта</button>}
        <Link className="btn" to={homeworkHref}><Icon name="homework" size={16} />Домашка</Link>
        <Link className="btn" to={chatHref}><Icon name="chat" size={16} />Обсудить в чате группы</Link>
        {dayHref && <Link className="btn quiet" to={dayHref}><Icon name="calendar" size={16} />Открыть день</Link>}
        {share && <ShareMenu card={share} label="Отправить другу" />}
      </div>
    </div>
  </Sheet>;
}
