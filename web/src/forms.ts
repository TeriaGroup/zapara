import type { FormAnswer, FormQuestion } from "./types";
export function validateFormQuestions(title: string, questions: FormQuestion[], deadline: string): string | null {
    if (!title.trim() || title.trim().length > 200)
        return "Введите название анкеты";
    if (questions.length < 1 || questions.length > 20 || new Set(questions.map(row => row.questionId)).size !== questions.length)
        return "Нужно от 1 до 20 разных вопросов";
    if (deadline && (!Number.isFinite(Date.parse(deadline)) || Date.parse(deadline) <= Date.now()))
        return "Срок должен быть в будущем";
    for (const question of questions) {
        if (!question.title.trim())
            return "У каждого вопроса должен быть текст";
        if (["singleChoice", "multipleChoice"].includes(question.kind)) {
            const options = question.options.map(option => option.trim());
            if (options.length < 2 || options.length > 10 || options.some(option => !option) || new Set(options).size !== options.length)
                return "Нужно от 2 до 10 непустых разных вариантов";
        }
    }
    return null;
}
export function validateFormAnswers(questions: FormQuestion[], answers: FormAnswer[]): string | null {
    if (new Set(answers.map(row => row.questionId)).size !== answers.length || answers.some(row => !questions.some(question => question.questionId === row.questionId)))
        return "Ответ содержит неизвестный или повторный вопрос";
    for (const question of questions) {
        const answer = answers.find(row => row.questionId === question.questionId);
        const text = answer?.text?.trim() || "", choices = answer?.choices ?? [];
        if (question.required && (question.kind.endsWith("Choice") ? choices.length === 0 : !text))
            return `Ответьте на вопрос «${question.title}»`;
        if (choices.some(choice => !question.options.includes(choice)) || new Set(choices).size !== choices.length || (question.kind === "singleChoice" && choices.length > 1))
            return "Проверьте варианты ответа";
        if ((question.kind === "shortText" && text.length > 1000) || text.length > 4000)
            return "Ответ слишком длинный";
    }
    return null;
}
