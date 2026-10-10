@extends('errors.layout')
{{-- #149: общий запасной вариант для 4xx без своей страницы (например, 405 на GET /admin/logout). --}}
@section('code', isset($exception) && method_exists($exception, 'getStatusCode') ? (string) $exception->getStatusCode() : '4xx')
@section('title', 'Запрос не выполнен')
@section('message', 'Панель не может обработать этот запрос. Вернитесь на инфопанель и повторите действие из меню.')
