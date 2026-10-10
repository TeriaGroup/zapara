import { useState } from "react";
import { Link } from "react-router-dom";
import * as api from "./api";
import { Icon } from "./icons";
import type { GroupFace } from "./groupChoice";

type Sending = "idle" | "sending" | "sent" | "failed";

/** /group без членства (#36): одно пустое состояние с действием вместо ошибки и «Повторить загрузку». */
export function GroupNoMembership({ missing, candidate, onRecheck }: {
  missing: NonNullable<GroupFace["missing"]>;
  candidate: GroupFace["candidate"];
  onRecheck: () => void;
}) {
  const [state, setState] = useState<Sending>("idle");
  if (missing === "group") return <div className="card empty group-no-membership">
    <Icon name="users" size={32} />
    <h2>Группа не выбрана</h2>
    <p>Выберите учебную группу — здесь появятся одногруппники, чат и голосования.</p>
    <Link className="btn primary" to="/settings?section=study">Выбрать группу</Link>
  </div>;
  async function send() {
    if (!candidate || state === "sending" || state === "sent") return;
    setState("sending");
    try { await api.joinCommunity(candidate.communityId); setState("sent"); }
    catch { setState("failed"); }
  }
  return <div className="card empty group-no-membership">
    <Icon name="users" size={32} />
    <h2>Вы ещё не в группе</h2>
    {!candidate ? <>
      <p>У этой группы пока нет сообщества в приложении. Его создаёт староста.</p>
      <div className="row"><Link className="btn primary" to="/community">Открыть «Сообщество»</Link>
        <Link className="btn" to="/settings?section=study">Выбрать другую группу</Link></div>
    </> : state === "sent" ? <>
      <p role="status">Заявка в «{candidate.name}» отправлена. Когда староста её примет, группа откроется здесь.</p>
      <button className="btn" type="button" onClick={onRecheck}>Проверить снова</button>
    </> : <>
      <p>Чат, голосования и общие задания откроются, когда староста примет заявку в «{candidate.name}».</p>
      {state === "failed" && <div className="banner" role="alert">Заявка не отправлена. Проверьте подключение и попробуйте ещё раз.</div>}
      <div className="row"><button className="btn primary" type="button" disabled={state === "sending"} onClick={() => void send()}>
        {state === "sending" ? "Отправляем…" : "Отправить заявку на вступление"}</button>
        <Link className="btn" to="/settings?section=study">Выбрать другую группу</Link></div>
    </>}
  </div>;
}
