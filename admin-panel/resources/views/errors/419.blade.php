@extends('errors.layout')
@section('code', '419')
@section('title', 'Сессия истекла')
@section('message', 'Страница была открыта слишком долго, и форма устарела. Войдите снова и повторите действие.')
@section('actions')
    <a class="zp-primary" href="{{ url('/admin/login') }}">Войти снова</a>
    <a href="{{ url('/admin') }}">На инфопанель</a>
@endsection
