﻿// Copyright (c) 2018 Jon P Smith, GitHub: JonPSmith, web: http://www.thereformedprogrammer.net/
// Licensed under MIT licence. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Linq.Expressions;
using GenericServices.Configuration;
using GenericServices.Configuration.Internal;
using GenericServices.Internal;
using GenericServices.Internal.Decoders;
using GenericServices.Internal.MappingCode;
using Microsoft.EntityFrameworkCore;

namespace GenericServices.PublicButHidden
{
    public class GenericService<TContext> : 
        StatusGenericHandler, 
        IGenericService<TContext> where TContext : DbContext
    {
        private readonly TContext _context;
        private readonly IWrappedAutoMapperConfig _wrapperMapperConfigs;
        private readonly IExpandedGlobalConfig _config;

        /// <summary>
        /// This allows you to access the current DbContext that this instance of the GenericService is using.
        /// That is useful if you need to set up some properties in the DTO that cannot be found in the Entity
        /// For instance, setting up a dropdownlist based on some other database data
        /// </summary>
        public TContext CurrentContext => _context;

        public GenericService(TContext context, IWrappedAutoMapperConfig wapper, IGenericServicesConfig config = null)
        {
            _context = context;
            _wrapperMapperConfigs = wapper ?? throw new ArgumentException(nameof(wapper));
            _config = new ExpandedGlobalConfig(config ?? new GenericServicesConfig(), context);
        }

        public T ReadSingle<T>(params object[] keys) where T : class
        {
            Header = "ReadSingle>Find";
            T result = null;
            var entityInfo = _context.GetUnderlyingEntityInfo(typeof(T));
            if (entityInfo.EntityType == typeof(T))
            {
                result = _context.Set<T>().Find(keys);
            }
            else
            {
                //else its a DTO, so we need to project the entity to the DTO and select the single element
                var projector = new CreateProjector(_context, _wrapperMapperConfigs.MapperReadConfig, typeof(T), entityInfo);
                result = ((IQueryable<T>) projector.Accessor.GetViaKeysWithProject(keys)).SingleOrDefault();
            }

            if (result == null)
            {
                AddError($"Sorry, I could not find the {ExtractDisplayHelpers.GetNameForClass<T>()} you were looking for.");
            }
            return result;
        }

        public T ReadSingle<T>(Expression<Func<T, bool>> whereExpression) where T : class
        {
            Header = "ReadSingle>Where";
            T result = null;
            var entityInfo = _context.GetUnderlyingEntityInfo(typeof(T));
            if (entityInfo.EntityType == typeof(T))
            {
                result = _context.Set<T>().Where(whereExpression).SingleOrDefault();
            }
            else
            {
                //else its a DTO, so we need to project the entity to the DTO and select the single element
                var projector = new CreateProjector(_context, _wrapperMapperConfigs.MapperReadConfig, typeof(T), entityInfo);
                result = ((IQueryable<T>)projector.Accessor.ProjectAndThenApplyWhereExpression(whereExpression)).SingleOrDefault();
            }

            if (result == null)
            {
                AddError($"Sorry, I could not find the {ExtractDisplayHelpers.GetNameForClass<T>()} you were looking for.");
            }
            return result;
        }

        public IQueryable<T> ReadManyNoTracked<T>() where T : class
        {
            Header = "ReadMany";
            var entityInfo = _context.GetUnderlyingEntityInfo(typeof(T));
            if (entityInfo.EntityType == typeof(T))
            {
                return _context.Set<T>().AsNoTracking();
            }

            //else its a DTO, so we need to project the entity to the DTO 
            var projector = new CreateProjector(_context, _wrapperMapperConfigs.MapperReadConfig, typeof(T), entityInfo);
            return projector.Accessor.GetManyProjectedNoTracking();
        }

        public T AddNewAndSave<T>(T entityOrDto, string ctorOrStaticMethodName = null) where T : class
        {
            Header = "AddNew";
            var entityInfo = _context.GetUnderlyingEntityInfo(typeof(T));
            if (entityInfo.EntityType == typeof(T))
            {
                _context.Add(entityOrDto);
                _context.SaveChanges();
            }
            else
            {
                var dtoInfo = typeof(T).GetDtoInfoThrowExceptionIfNotThere();
                var creator = new EntityCreateHandler<T>(dtoInfo, entityInfo, _wrapperMapperConfigs, _config);
                var entity = creator.CreateEntityAndFillFromDto(entityOrDto, ctorOrStaticMethodName);
                CombineStatuses(creator);
                if (IsValid)
                {
                    _context.Add(entity);
                    CombineStatuses(_context.SaveChangesWithOptionalValidation(dtoInfo.ValidateOnSave));
                    if (IsValid)
                        entity.CopyBackKeysFromEntityToDtoIfPresent(entityOrDto, entityInfo);
                }
            }
            return IsValid ? entityOrDto : null;
        }

        public void UpdateAndSave<T>(T entityOrDto, string methodName = null) where T : class
        {
            Header = "Update";
            var entityInfo = _context.GetUnderlyingEntityInfo(typeof(T));
            if (entityInfo.EntityType == typeof(T))
            {
                if (_context.Entry(entityOrDto).State == EntityState.Detached)
                    _context.Update(entityOrDto);
                _context.SaveChanges();
            }
            else
            {
                var dtoInfo = typeof(T).GetDtoInfoThrowExceptionIfNotThere();
                var updater = new EntityUpdateHandler<T>(dtoInfo, entityInfo, _wrapperMapperConfigs, _config);
                CombineStatuses(updater.ReadEntityAndUpdateViaDto(entityOrDto, methodName));
                if (IsValid)
                    CombineStatuses(_context.SaveChangesWithOptionalValidation(dtoInfo.ValidateOnSave));        
            }
        }

        public void DeleteAndSave<T>(params object[] keys) where T : class
        {
            Header = "Delete";
            var entityInfo = _context.GetUnderlyingEntityInfo(typeof(T));
            if (entityInfo.EntityType == typeof(T))
            {
                var entity = _context.Set<T>().Find(keys);
                if (entity == null)
                {
                    AddError($"Sorry, I could not find the {ExtractDisplayHelpers.GetNameForClass<T>()} you wanted to delete.");
                    return;
                }
                _context.Remove(entity);
                _context.SaveChanges();
                return;
            }

            throw new NotImplementedException("You cannot delete a DTO/ViewModel. You must provide a real entity class.");
        }

    }
}