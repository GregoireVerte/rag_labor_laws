-- 1. Funkcja triggera (Wprowadza 'Aktywny' i 'Standard' oraz bezpieczny ON CONFLICT)
create or replace function public.handle_new_user()
returns trigger as $$
begin
  insert into public."Users" ("Id", "Email", "Status", "Role", "TelegramChatId", "ActiveConsultationId")
  values (new.id, new.email, 'Aktywny', 'Standard', null, null)
  on conflict ("Email") 
  do update set "Status" = 'Aktywny'; -- Aktualizuje tylko status, nie ruszam ID ani Roli Administratora
  return new;
end;
$$ language plpgsql security definer;

-- 2. Tworzy automat (trigger), który odpali tę funkcję po każdej udanej rejestracji
create or replace trigger on_auth_user_created
  after insert on auth.users
  for each row execute procedure public.handle_new_user();