import type { FormQuestion, FormResponse } from "./types";
function cell(value: string) {
    const safe = /^[\s\u0000-\u001f\u007f]*[=+@-]/.test(value) ? "'" + value : value;
    return '"' + safe.replaceAll('"', '""') + '"';
}
export function formResponsesCsv(questions: FormQuestion[], responses: FormResponse[]) {
    const header = ["Участник", "Время ответа", ...questions.map(question => question.title)];
    const rows = responses.map(response => [response.respondentId || "Анонимный ответ", response.updatedAt, ...questions.map(question => {
        const answer = response.answers.find(row => row.questionId === question.questionId);
        return answer?.text || answer?.choices.join(", ") || "";
    })]);
    return "\ufeff" + [header, ...rows].map(row => row.map(cell).join(";")).join("\r\n");
}
