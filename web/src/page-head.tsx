import type { ReactNode } from "react";
import { createPortal } from "react-dom";
import { useMobileScreenTitle } from "./mobile-chrome";

export function PageHead({ title, text, children, mobileActions = false }: {
  title: string; text?: string; children?: ReactNode; mobileActions?: boolean;
}) {
  const chrome = useMobileScreenTitle(title);
  const portal = mobileActions && chrome?.compact && chrome.actionsHost;
  const actions = children && <div className="row controls">{children}</div>;
  return <div className={"page-head" + (chrome ? " mobile-page-head" : "")}>
    <div className="page-head-copy"><h1>{title}</h1>{text && <p className="sub">{text}</p>}</div>
    {portal ? createPortal(actions, portal) : actions}
  </div>;
}
